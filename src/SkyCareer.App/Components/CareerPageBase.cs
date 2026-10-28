using Microsoft.AspNetCore.Components;
using MudBlazor;
using SkyCareer.Application;

namespace SkyCareer.App.Components;

/// <summary>
/// Shared plumbing for career pages: reloads when the career changes and turns <see cref="CareerException"/>s
/// into snackbar messages instead of error screens.
/// </summary>
public abstract class CareerPageBase : ComponentBase, IDisposable
{
    [Inject] protected CareerService Career { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    protected bool Busy { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        Career.Changed += OnCareerChanged;
        await SafeLoad();
    }

    /// <summary>Loads the page's data. Only called while a career is active, except on pages that override <see cref="NeedsCareer"/>.</summary>
    protected abstract Task LoadAsync();

    protected virtual bool NeedsCareer => true;

    private void OnCareerChanged() => InvokeAsync(async () =>
    {
        await SafeLoad();
        StateHasChanged();
    });

    private async Task SafeLoad()
    {
        if (NeedsCareer && Career.ActiveCareerId is null) return;
        try { await LoadAsync(); }
        catch (CareerException ex) { Snackbar.Add(ex.Message, Severity.Warning); }
    }

    protected async Task Run(Func<Task> action, string? success = null)
    {
        Busy = true;
        try
        {
            await action();
            if (success is not null) Snackbar.Add(success, Severity.Success);
        }
        catch (Exception ex) when (ex is CareerException or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            Busy = false;
        }
    }

    protected static string Eur(decimal amount) => $"{amount:N0} €";

    public virtual void Dispose()
    {
        Career.Changed -= OnCareerChanged;
        GC.SuppressFinalize(this);
    }
}
