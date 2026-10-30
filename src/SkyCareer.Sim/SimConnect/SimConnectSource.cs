#if HAS_SIMCONNECT
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using SkyCareer.Domain;
using SkyCareer.Domain.Loading;

namespace SkyCareer.Sim.SimConnectIntegration;

/// <summary>
/// Live connection to MSFS 2024. Runs its own message pump thread, reconnects automatically when the sim
/// starts or restarts, and converts raw SimVars (pounds, feet/second, …) into <see cref="Telemetry"/>.
/// </summary>
public sealed class SimConnectSource : IFlightDataSource, IAircraftLoader, IAircraftInspector, IAirportFacilityReader
{
    private enum Definition { Telemetry = 1, FuelTankWrite = 3, AirportFacility = 4, StationBase = 100 }
    private enum Request { Telemetry = 1 }
    private enum SysEvent { Pause = 1, Crashed = 2, SimStart = 3 }

    /// <summary>One-shot requests get ids from here up so they never collide with the fixed ones above.</summary>
    private const int DynamicIdStart = 1000;

    private static readonly string[] TankNames =
    [
        "CENTER", "CENTER2", "CENTER3", "LEFT MAIN", "RIGHT MAIN",
        "LEFT AUX", "RIGHT AUX", "LEFT TIP", "RIGHT TIP", "EXTERNAL1", "EXTERNAL2"
    ];

    private readonly Lock _lock = new();
    private readonly HashSet<int> _registeredStations = [];
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<object>> _pending = new();
    private readonly ConcurrentDictionary<uint, uint> _packetToRequest = new();
    private readonly ConcurrentDictionary<uint, FacilityRequest> _facilityRequests = new();
    /// <summary>Facility requests are serialized: the sim answers them as streams of records, one airport at a time is simplest.</summary>
    private readonly SemaphoreSlim _facilityGate = new(1, 1);
    private int _nextId = DynamicIdStart;
    private SimConnect? _sc;
    private EventWaitHandle? _signal;
    private Thread? _pump;
    private CancellationTokenSource? _cts;
    private bool _paused, _crashed, _quit;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    /// <summary>Set when the pump stopped because of an unexpected error (e.g. the SimConnect library failed to load).</summary>
    public string? LastError { get; private set; }
    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<Telemetry>? TelemetryReceived;

    /// <summary>Forces the managed and native SimConnect libraries to load. Returns null on success, otherwise the error.</summary>
    public static string? ProbeLibrary()
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(typeof(SimConnect).TypeHandle);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset);
        _pump = new Thread(() => Pump(_cts.Token)) { IsBackground = true, Name = "SimConnect pump" };
        _pump.Start();
        return Task.CompletedTask;
    }

    private void Pump(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_sc is null && !TryConnect())
                {
                    ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    continue;
                }

                if (WaitHandle.WaitAny([_signal!, ct.WaitHandle], TimeSpan.FromSeconds(1)) == 1) break;
                try
                {
                    lock (_lock) _sc?.ReceiveMessage();
                }
                catch (COMException)
                {
                    _quit = true;
                }
                // Disposing inside a ReceiveMessage callback is unsafe, so quits are handled here.
                if (_quit)
                {
                    _quit = false;
                    Disconnect();
                }
            }
        }
        catch (Exception ex)
        {
            // An exception on this background thread would kill the whole app; report it instead.
            LastError = $"{ex.GetType().Name}: {ex.Message}";
        }
        Disconnect();
    }

    private bool TryConnect()
    {
        SetState(ConnectionState.Connecting);
        try
        {
            lock (_lock)
            {
                _sc = new SimConnect("SkyCareer", IntPtr.Zero, 0, _signal, 0);
                _sc.OnRecvOpen += (_, _) => SetState(ConnectionState.Connected);
                _sc.OnRecvQuit += (_, _) => _quit = true;
                _sc.OnRecvSimobjectData += OnData;
                _sc.OnRecvEvent += OnEvent;
                _sc.OnRecvException += OnException;
                _sc.OnRecvFacilityData += OnFacilityData;
                _sc.OnRecvFacilityDataEnd += OnFacilityDataEnd;
                RegisterTelemetry(_sc);
                RegisterTankWrite(_sc);
                RegisterAirportFacility(_sc);
                _sc.SubscribeToSystemEvent(SysEvent.Pause, "Pause_EX1");
                _sc.SubscribeToSystemEvent(SysEvent.Crashed, "Crashed");
                _sc.SubscribeToSystemEvent(SysEvent.SimStart, "SimStart");
                // ~4 Hz at 60 fps; touchdown rate comes from PLANE TOUCHDOWN NORMAL VELOCITY so high frequency isn't needed.
                _sc.RequestDataOnSimObject(Request.Telemetry, Definition.Telemetry, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 15, 0);
            }
            return true;
        }
        catch (COMException)
        {
            _sc = null;
            SetState(ConnectionState.Disconnected);
            return false;
        }
    }

    private static void RegisterTelemetry(SimConnect sc)
    {
        void Add(string name, string unit, SIMCONNECT_DATATYPE type = SIMCONNECT_DATATYPE.FLOAT64) =>
            sc.AddToDataDefinition(Definition.Telemetry, name, unit, type, 0, SimConnect.SIMCONNECT_UNUSED);

        // Order must match RawTelemetry exactly.
        Add("PLANE LATITUDE", "degrees");
        Add("PLANE LONGITUDE", "degrees");
        Add("PLANE ALTITUDE", "feet");
        Add("PLANE ALT ABOVE GROUND", "feet");
        Add("GROUND VELOCITY", "knots");
        Add("AIRSPEED INDICATED", "knots");
        Add("VERTICAL SPEED", "feet per minute");
        Add("G FORCE", "GForce");
        Add("SIM ON GROUND", "bool");
        Add("BRAKE PARKING POSITION", "bool");
        Add("GENERAL ENG COMBUSTION:1", "bool");
        Add("GENERAL ENG COMBUSTION:2", "bool");
        Add("GENERAL ENG COMBUSTION:3", "bool");
        Add("GENERAL ENG COMBUSTION:4", "bool");
        Add("FUEL TOTAL QUANTITY WEIGHT", "pounds");
        Add("TOTAL WEIGHT", "pounds");
        Add("EMPTY WEIGHT", "pounds");
        Add("PLANE TOUCHDOWN NORMAL VELOCITY", "feet per second");
        Add("SIMULATION RATE", "number");
        Add("IS SLEW ACTIVE", "bool");
        Add("OVERSPEED WARNING", "bool");
        Add("STALL WARNING", "bool");
        Add("TITLE", null!, SIMCONNECT_DATATYPE.STRING256);
        sc.RegisterDataDefineStruct<RawTelemetry>(Definition.Telemetry);
    }

    private static void RegisterTankWrite(SimConnect sc)
    {
        foreach (var tank in TankNames)
            sc.AddToDataDefinition(Definition.FuelTankWrite, $"FUEL TANK {tank} QUANTITY", "gallons", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
        sc.RegisterDataDefineStruct<TankValues>(Definition.FuelTankWrite);
    }

    /// <summary>SimConnect approach type for ILS (1 GPS, 2 VOR, 3 NDB, 4 ILS, 5 LOC, … 10 RNAV).</summary>
    private const int ApproachTypeIls = 4;

    // Field order must match AirportFacilityRaw / RunwayFacilityRaw / ApproachFacilityRaw. Verified live against
    // MSFS 2024: EDDF ILS on 07/25 (none on 18), EDFE none, LSZH only via approaches (runway ILS fields empty),
    // unknown idents answer with an exception and no end record.
    private static void RegisterAirportFacility(SimConnect sc)
    {
        foreach (var field in new[]
                 {
                     "OPEN AIRPORT", "N_RUNWAYS",
                     "OPEN RUNWAY", "PRIMARY_NUMBER", "PRIMARY_DESIGNATOR", "LENGTH", "SURFACE", "PRIMARY_ILS_ICAO",
                     "SECONDARY_NUMBER", "SECONDARY_DESIGNATOR", "SECONDARY_ILS_ICAO", "CLOSE RUNWAY",
                     "OPEN APPROACH", "TYPE", "RUNWAY_NUMBER", "RUNWAY_DESIGNATOR", "CLOSE APPROACH",
                     "CLOSE AIRPORT"
                 })
            sc.AddToFacilityDefinition(Definition.AirportFacility, field);
        sc.RegisterFacilityDataDefineStruct<AirportFacilityRaw>(SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT);
        sc.RegisterFacilityDataDefineStruct<RunwayFacilityRaw>(SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY);
        sc.RegisterFacilityDataDefineStruct<ApproachFacilityRaw>(SIMCONNECT_FACILITY_DATA_TYPE.APPROACH);
    }

    public async Task<AirportFacilityInfo?> ReadAirportAsync(string icao, CancellationToken ct = default)
    {
        await _facilityGate.WaitAsync(ct).ConfigureAwait(false);
        uint id = (uint)Interlocked.Increment(ref _nextId);
        var request = new FacilityRequest(icao.ToUpperInvariant());
        _facilityRequests[id] = request;
        try
        {
            lock (_lock)
            {
                var sc = _sc ?? throw new InvalidOperationException("Not connected to the simulator.");
                sc.RequestFacilityData(Definition.AirportFacility, (Request)id, request.Icao, "");
                _packetToRequest[sc.GetLastSentPacketID()] = id;
            }
            return await request.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
        finally
        {
            _facilityRequests.TryRemove(id, out _);
            foreach (var kv in _packetToRequest.Where(kv => kv.Value == id)) _packetToRequest.TryRemove(kv.Key, out _);
            _facilityGate.Release();
        }
    }

    private void OnFacilityData(SimConnect sender, SIMCONNECT_RECV_FACILITY_DATA data)
    {
        if (!_facilityRequests.TryGetValue(data.UserRequestId, out var request)) return;
        switch ((SIMCONNECT_FACILITY_DATA_TYPE)data.Type)
        {
            case SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY:
                var r = (RunwayFacilityRaw)data.Data[0];
                request.Runways.Add(new SimRunway(
                    $"{RunwayEnd(r.PrimaryNumber, r.PrimaryDesignator)}/{RunwayEnd(r.SecondaryNumber, r.SecondaryDesignator)}",
                    r.Length, r.Surface, Ident(r.PrimaryIls), Ident(r.SecondaryIls)));
                break;
            case SIMCONNECT_FACILITY_DATA_TYPE.APPROACH:
                var a = (ApproachFacilityRaw)data.Data[0];
                if (a.Type == ApproachTypeIls) request.IlsApproachRunways.Add(RunwayEnd(a.RunwayNumber, a.RunwayDesignator));
                break;
        }
    }

    private void OnFacilityDataEnd(SimConnect sender, SIMCONNECT_RECV_FACILITY_DATA_END data)
    {
        if (_facilityRequests.TryGetValue(data.RequestId, out var request))
            request.Completion.TrySetResult(new AirportFacilityInfo(request.Icao, request.Runways.ToList(),
                request.IlsApproachRunways.Distinct().Order().ToList()));
    }

    /// <summary>Runway end like "07C": number plus designator (1 = L, 2 = R, 3 = C, 4 = water).</summary>
    private static string RunwayEnd(int number, int designator) =>
        number.ToString("00") + designator switch { 1 => "L", 2 => "R", 3 => "C", 4 => "W", _ => "" };

    /// <summary>
    /// Navaid ident from a char[8] field. Read as bytes and cut at the first NUL: decoding the field as a string
    /// produced stray characters for runway ends without ILS.
    /// </summary>
    private static string? Ident(byte[]? raw)
    {
        if (raw is null) return null;
        var text = new string(raw.TakeWhile(b => b != 0).Select(b => (char)b).ToArray()).Trim();
        return text.Length >= 2 && text.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9') ? text : null;
    }

    private sealed class FacilityRequest(string icao)
    {
        public string Icao { get; } = icao;
        public List<SimRunway> Runways { get; } = [];
        public List<string> IlsApproachRunways { get; } = [];
        public TaskCompletionSource<AirportFacilityInfo?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void OnData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwRequestID == (uint)Request.Telemetry)
            TelemetryReceived?.Invoke(this, Convert((RawTelemetry)data.dwData[0]));
        else if (_pending.TryRemove(data.dwRequestID, out var tcs))
            tcs.TrySetResult(data.dwData[0]);
    }

    private void OnEvent(SimConnect sender, SIMCONNECT_RECV_EVENT e)
    {
        switch ((SysEvent)e.uEventID)
        {
            case SysEvent.Pause: _paused = e.dwData != 0; break;
            case SysEvent.Crashed: _crashed = true; break;
            case SysEvent.SimStart: _crashed = false; break;
        }
    }

    /// <summary>SimConnect reports errors asynchronously by packet id; fail the one-shot request that sent it.</summary>
    private void OnException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION e)
    {
        if (!_packetToRequest.TryRemove(e.dwSendID, out var requestId)) return;
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.TrySetException(new InvalidOperationException($"SimConnect rejected the request: {(SIMCONNECT_EXCEPTION)e.dwException}"));
        // An airport the sim doesn't know comes back as an exception without an end record.
        else if (_facilityRequests.TryGetValue(requestId, out var facility))
            facility.Completion.TrySetResult(null);
    }

    /// <summary>
    /// Defines a temporary data definition, requests it once for the user aircraft and awaits the result.
    /// <paramref name="define"/> adds the variables; it must call the supplied tracker after each SimConnect call
    /// so exceptions can be matched back to this request.
    /// </summary>
    private async Task<T> RequestOnceAsync<T>(Action<SimConnect, Definition, Action> define, CancellationToken ct) where T : struct
    {
        uint id = (uint)Interlocked.Increment(ref _nextId);
        var def = (Definition)id;
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        lock (_lock)
        {
            var sc = _sc ?? throw new InvalidOperationException("Not connected to the simulator.");
            void Track()
            {
                _packetToRequest[sc.GetLastSentPacketID()] = id;
            }
            define(sc, def, Track);
            sc.RegisterDataDefineStruct<T>(def);
            sc.RequestDataOnSimObject((Request)id, def, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            Track();
        }

        try
        {
            return (T)await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        finally
        {
            _pending.TryRemove(id, out _);
            foreach (var kv in _packetToRequest.Where(kv => kv.Value == id)) _packetToRequest.TryRemove(kv.Key, out _);
            lock (_lock) _sc?.ClearDataDefinition(def);
        }
    }

    private static Action<SimConnect, Definition, Action> Vars(params (string Name, string? Unit, SIMCONNECT_DATATYPE Type)[] vars) =>
        (sc, def, track) =>
        {
            foreach (var (name, unit, type) in vars)
            {
                sc.AddToDataDefinition(def, name, unit!, type, 0, SimConnect.SIMCONNECT_UNUSED);
                track();
            }
        };

    private static (string, string?, SIMCONNECT_DATATYPE) F64(string name, string unit) => (name, unit, SIMCONNECT_DATATYPE.FLOAT64);
    private static (string, string?, SIMCONNECT_DATATYPE) Str(string name) => (name, null, SIMCONNECT_DATATYPE.STRING256);

    public async Task<double> ReadVariableAsync(string name, string unit = "number", CancellationToken ct = default) =>
        (await RequestOnceAsync<SingleValue>(Vars(F64(name, unit)), ct)).Value;

    public async Task<double[]> ReadVariablesAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (names.Count > IAircraftInspector.MaxBatchVariables)
            throw new ArgumentException($"At most {IAircraftInspector.MaxBatchVariables} variables per batch.", nameof(names));
        // The struct always has 16 fields, so pad with an unused L:Var (reads as 0) to keep the layout fixed.
        var padded = names.Concat(Enumerable.Range(0, IAircraftInspector.MaxBatchVariables - names.Count).Select(i => $"L:SKYCAREER_UNUSED_{i}"))
            .Select(n => F64(n, "number")).ToArray();
        var v = await RequestOnceAsync<Batch16>(Vars(padded), ct);
        return v.ToArray()[..names.Count];
    }

    public async Task<AircraftSnapshot> ReadAircraftAsync(CancellationToken ct = default)
    {
        var info = await RequestOnceAsync<AircraftInfo>(Vars(
            Str("TITLE"), Str("ATC MODEL"), Str("ATC TYPE"),
            F64("EMPTY WEIGHT", "pounds"), F64("MAX GROSS WEIGHT", "pounds"), F64("TOTAL WEIGHT", "pounds"),
            F64("FUEL TOTAL QUANTITY WEIGHT", "pounds"), F64("FUEL WEIGHT PER GALLON", "pounds"),
            F64("PAYLOAD STATION COUNT", "number")), ct);

        var stations = new List<SimPayloadStation>();
        for (int i = 1; i <= (int)info.StationCount; i++)
        {
            var s = await RequestOnceAsync<StationRaw>(Vars(Str($"PAYLOAD STATION NAME:{i}"), F64($"PAYLOAD STATION WEIGHT:{i}", "pounds")), ct);
            stations.Add(new SimPayloadStation(i, s.Name ?? "", Mass.FromLb(s.WeightLb).Kg));
        }

        var caps = (await ReadTanksAsync("CAPACITY", ct)).ToArray();
        var qty = (await ReadTanksAsync("QUANTITY", ct)).ToArray();
        var tanks = TankNames.Select((n, i) => new SimFuelTank(n, caps[i], qty[i])).Where(t => t.CapacityGallons > 0).ToList();

        return new AircraftSnapshot(info.Title ?? "", info.AtcModel ?? "", info.AtcType ?? "",
            Mass.FromLb(info.EmptyLb).Kg, Mass.FromLb(info.MaxGrossLb).Kg, Mass.FromLb(info.TotalLb).Kg,
            Mass.FromLb(info.FuelLb).Kg, Mass.FromLb(info.LbPerGallon).Kg, stations, tanks, await ReadDesignAsync(ct));
    }

    /// <summary>Separate request so an aircraft lacking one of these variables doesn't break the whole snapshot.</summary>
    private async Task<AircraftDesignInfo?> ReadDesignAsync(CancellationToken ct)
    {
        try
        {
            var d = await RequestOnceAsync<DesignRaw>(Vars(
                Str("CATEGORY"), F64("ENGINE TYPE", "enum"), F64("NUMBER OF ENGINES", "number"),
                F64("DESIGN SPEED VC", "knots"), F64("DESIGN CRUISE ALT", "feet")), ct);
            return new AircraftDesignInfo((SimEngineType)(int)d.EngineType, (int)d.EngineCount, d.Category ?? "", d.CruiseSpeed, d.CruiseAlt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    private async Task<double[]> ReadTanksAsync(string field, CancellationToken ct)
    {
        var v = await RequestOnceAsync<TankValues>(Vars(TankNames.Select(t => F64($"FUEL TANK {t} {field}", "gallons")).ToArray()), ct);
        return v.ToArray();
    }

    private Telemetry Convert(RawTelemetry r)
    {
        double fuelKg = Mass.FromLb(r.FuelLb).Kg;
        double totalKg = Mass.FromLb(r.TotalWeightLb).Kg;
        return new Telemetry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            AltitudeFt = r.AltitudeFt,
            AltitudeAglFt = r.AltitudeAglFt,
            GroundSpeedKt = r.GroundSpeedKt,
            IndicatedAirspeedKt = r.IasKt,
            VerticalSpeedFpm = r.VsFpm,
            GForce = r.GForce,
            OnGround = r.OnGround != 0,
            ParkingBrake = r.ParkingBrake != 0,
            AnyEngineRunning = r.Eng1 != 0 || r.Eng2 != 0 || r.Eng3 != 0 || r.Eng4 != 0,
            FuelKg = fuelKg,
            TotalWeightKg = totalKg,
            PayloadKg = Math.Max(0, totalKg - Mass.FromLb(r.EmptyWeightLb).Kg - fuelKg),
            TouchdownVsFpm = -Math.Abs(r.TouchdownFps) * 60,
            SimulationRate = r.SimRate,
            Slew = r.Slew != 0,
            Paused = _paused,
            Crashed = _crashed,
            OverspeedWarning = r.Overspeed != 0,
            StallWarning = r.Stall != 0,
            AircraftTitle = r.Title ?? ""
        };
    }

    public Task ApplyPayloadAsync(IReadOnlyList<StationLoad> stations, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var sc = _sc ?? throw new InvalidOperationException("Not connected to the simulator.");
            foreach (var s in stations)
            {
                var def = (Definition)((int)Definition.StationBase + s.Index);
                if (_registeredStations.Add(s.Index))
                {
                    sc.AddToDataDefinition(def, $"PAYLOAD STATION WEIGHT:{s.Index}", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
                    sc.RegisterDataDefineStruct<SingleValue>(def);
                }
                sc.SetDataOnSimObject(def, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new SingleValue { Value = s.Mass.Lb });
            }
        }
        return Task.CompletedTask;
    }

    public async Task ApplyFuelAsync(Mass totalFuel, CancellationToken ct = default)
    {
        double[] caps = await ReadTanksAsync("CAPACITY", ct);
        double lbPerGallon = await ReadVariableAsync("FUEL WEIGHT PER GALLON", "pounds", ct);
        double totalCap = caps.Sum();
        if (totalCap <= 0 || lbPerGallon <= 0)
            throw new InvalidOperationException("Aircraft reports no legacy fuel tank capacity (it may use the new fuel system or a custom one).");

        double gallons = Math.Min(totalFuel.Lb / lbPerGallon, totalCap);
        // Proportional fill keeps the fuel balanced; aircraft with wing-first fill rules need an LVar profile.
        var q = caps.Select(c => gallons * c / totalCap).ToArray();

        lock (_lock)
            _sc?.SetDataOnSimObject(Definition.FuelTankWrite, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT, TankValues.From(q));
    }

    public async Task WriteVariableAsync(string name, string unit, double value, CancellationToken ct = default)
    {
        uint id = (uint)Interlocked.Increment(ref _nextId);
        var def = (Definition)id;
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        lock (_lock)
        {
            var sc = _sc ?? throw new InvalidOperationException("Not connected to the simulator.");
            sc.AddToDataDefinition(def, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            _packetToRequest[sc.GetLastSentPacketID()] = id;
            sc.RegisterDataDefineStruct<SingleValue>(def);
            sc.SetDataOnSimObject(def, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new SingleValue { Value = value });
            _packetToRequest[sc.GetLastSentPacketID()] = id;
        }
        try
        {
            // Writes have no answer; only a rejection arrives (as an exception). No news within a second counts as sent.
            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1), ct)).ConfigureAwait(false);
            if (tcs.Task.IsFaulted) await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
            foreach (var kv in _packetToRequest.Where(kv => kv.Value == id)) _packetToRequest.TryRemove(kv.Key, out _);
            lock (_lock) _sc?.ClearDataDefinition(def);
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_pump is not null) await Task.Run(() => _pump.Join(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _signal?.Dispose();
        _cts?.Dispose();
    }

    private void Disconnect()
    {
        lock (_lock)
        {
            _sc?.Dispose();
            _sc = null;
            _registeredStations.Clear();
        }
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var tcs)) tcs.TrySetException(new InvalidOperationException("Simulator disconnected."));
        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(this, s);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct RawTelemetry
    {
        public double Latitude, Longitude, AltitudeFt, AltitudeAglFt, GroundSpeedKt, IasKt, VsFpm, GForce;
        public double OnGround, ParkingBrake, Eng1, Eng2, Eng3, Eng4;
        public double FuelLb, TotalWeightLb, EmptyWeightLb, TouchdownFps, SimRate, Slew, Overspeed, Stall;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct AircraftInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AtcModel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AtcType;
        public double EmptyLb, MaxGrossLb, TotalLb, FuelLb, LbPerGallon, StationCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AirportFacilityRaw { public int RunwayCount; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RunwayFacilityRaw
    {
        public int PrimaryNumber, PrimaryDesignator;
        public float Length;
        public int Surface;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PrimaryIls;
        public int SecondaryNumber, SecondaryDesignator;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] SecondaryIls;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ApproachFacilityRaw { public int Type, RunwayNumber, RunwayDesignator; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct DesignRaw
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Category;
        public double EngineType, EngineCount, CruiseSpeed, CruiseAlt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct StationRaw
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        public double WeightLb;
    }

    /// <summary>One value per entry of <see cref="TankNames"/>, in that order.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TankValues
    {
        public double Center, Center2, Center3, LeftMain, RightMain, LeftAux, RightAux, LeftTip, RightTip, External1, External2;

        public readonly double[] ToArray() =>
            [Center, Center2, Center3, LeftMain, RightMain, LeftAux, RightAux, LeftTip, RightTip, External1, External2];

        public static TankValues From(double[] q) => new()
        {
            Center = q[0], Center2 = q[1], Center3 = q[2], LeftMain = q[3], RightMain = q[4],
            LeftAux = q[5], RightAux = q[6], LeftTip = q[7], RightTip = q[8], External1 = q[9], External2 = q[10]
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SingleValue { public double Value; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Batch16
    {
        public double V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15;

        public readonly double[] ToArray() => [V0, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15];
    }
}
#endif
