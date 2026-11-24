// Leaflet wrapper for the mission market map. Blazor owns the data; this file only draws it.
window.skyMap = (() => {
    const maps = {};

    // labels: translated layer names { dark, streets, terrain } from MissionMap.razor.
    function init(elementId, dotNetRef, labels) {
        if (maps[elementId]) return;
        // Canvas renderer with a click tolerance so thin route lines are easy to hit.
        const map = L.map(elementId, { worldCopyJump: true, zoomControl: true, renderer: L.canvas({ tolerance: 8 }) }).setView([50, 10], 5);
        // Keyless tile sources only (CARTO's basemaps now require an API key). Dark is the default to match the app.
        const t = labels || {};
        const dark = t.dark || 'Dark', streets = t.streets || 'Streets', terrain = t.terrain || 'Terrain';
        const baseLayers = {
            [dark]: L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}', {
                maxZoom: 16,
                attribution: 'Tiles &copy; Esri &mdash; Esri, DeLorme, NAVTEQ'
            }),
            [streets]: L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            }),
            [terrain]: L.tileLayer('https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png', {
                maxZoom: 17,
                subdomains: 'abc',
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>, SRTM | &copy; <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)'
            })
        };
        baseLayers[dark].addTo(map);
        L.control.layers(baseLayers, null, { position: 'topright' }).addTo(map);

        maps[elementId] = {
            map,
            dotNetRef,
            layers: L.layerGroup().addTo(map),
            live: null,
            fitted: false
        };
    }

    // All popup text comes from mission data; escape it before building HTML.
    function esc(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    // labels: translated popup texts { accept, accepted, show, oneJob, jobs ("{0}" = count) } from MissionMap.razor.
    function popupHtml(group, labels) {
        const t = labels || {};
        const items = group.offers.map(o => `
            <div class="sc-pop-item${o.selected ? ' sc-pop-selected' : ''}">
                <div class="sc-pop-title"><span style="color:${esc(o.color)}">●</span> ${esc(o.title)}
                    <span class="sc-pop-pay">${esc(o.payout)}</span></div>
                ${o.details.map(d => `<div class="sc-pop-line">${esc(d)}</div>`).join('')}
                <div class="sc-pop-actions">
                    ${o.canAccept ? `<button type="button" class="sc-pop-btn sc-pop-primary" data-action="accept" data-id="${esc(o.id)}">${esc(t.accept || 'Accept')}</button>` : `<span class="sc-pop-line">${esc(t.accepted || 'accepted')}</span>`}
                    <button type="button" class="sc-pop-btn" data-action="show" data-id="${esc(o.id)}">${esc(t.show || 'Show in list')}</button>
                </div>
            </div>`).join('');
        return `<div class="sc-pop">
            <div class="sc-pop-head">${esc(group.destIdent)} · ${esc(group.destName)}</div>
            <div class="sc-pop-sub">${esc(group.offers.length === 1 ? (t.oneJob || '1 job') : (t.jobs || '{0} jobs').replace('{0}', group.offers.length))}</div>
            <div class="sc-pop-list">${items}</div>
        </div>`;
    }

    function update(elementId, data) {
        const m = maps[elementId];
        if (!m) return;
        m.layers.clearLayers();
        const bounds = [];
        const groups = new Map();

        for (const o of data.offers) {
            bounds.push(o.from, o.to);
            // Only accepted jobs and the selected offer get their route drawn; the others show as destination pins,
            // so a busy market stays readable.
            if (o.accepted || o.selected) {
                const line = L.polyline(o.path || [o.from, o.to], {
                    color: o.color,
                    weight: 4,
                    opacity: 1,
                    dashArray: o.accepted ? null : '6 6'
                }).addTo(m.layers);
                line.bindTooltip(o.label, { sticky: true });
                line.on('click', () => m.dotNetRef.invokeMethodAsync('OnOfferClicked', o.id));
                // Accepted jobs: the arrival radius (3 nm) the settlement checks the parking position against.
                if (o.accepted) {
                    L.circle(o.to, { radius: 5556, color: o.color, weight: 1.5, opacity: 0.9, fillOpacity: 0.08, interactive: false }).addTo(m.layers);
                }
                // Waypoints of a round trip: small rings along the path.
                for (const p of (o.path || []).slice(1, -1)) {
                    L.circleMarker(p, { radius: 4, color: o.color, weight: 2, fill: false, interactive: false }).addTo(m.layers);
                }
            }

            if (!groups.has(o.destIdent)) groups.set(o.destIdent, { destIdent: o.destIdent, destName: o.destName, to: o.to, offers: [] });
            groups.get(o.destIdent).offers.push(o);
        }

        // One pin per destination; its popup lists every job going there.
        for (const g of groups.values()) {
            const colors = [...new Set(g.offers.map(o => o.color))];
            const count = g.offers.length;
            const pin = L.circleMarker(g.to, {
                radius: 5 + Math.min(count, 6) * 1.2,
                color: '#ffffff',
                weight: g.offers.some(o => o.selected) ? 3 : 1.5,
                fillColor: colors.length === 1 ? colors[0] : '#b0b4c0',
                fillOpacity: 0.95
            }).addTo(m.layers);
            if (count > 1) pin.bindTooltip(String(count), { permanent: true, direction: 'center', className: 'sc-pin-count' });
            else pin.bindTooltip(`${g.destIdent} · ${g.offers[0].title}`);
            pin.bindPopup(() => popupHtml(g, data.labels), { maxWidth: 360, minWidth: 260, className: 'sc-popup' });
            pin.on('popupopen', e => {
                for (const btn of e.popup.getElement().querySelectorAll('button[data-action]')) {
                    btn.addEventListener('click', () => {
                        const id = btn.dataset.id;
                        if (btn.dataset.action === 'accept') {
                            m.dotNetRef.invokeMethodAsync('OnAcceptClicked', id);
                            m.map.closePopup();
                        } else {
                            m.dotNetRef.invokeMethodAsync('OnShowClicked', id);
                        }
                    });
                }
            });
        }

        // Regions in demand this week: a golden outline with a label. Neither takes clicks, so the jobs
        // and pins inside stay clickable.
        for (const e of (data.events || [])) {
            L.rectangle([[e.south, e.west], [e.north, e.east]], {
                color: '#ffc107', weight: 1.5, dashArray: '6 4', fillColor: '#ffc107', fillOpacity: 0.07, interactive: false
            }).addTo(m.layers);
            L.tooltip([e.north, (e.west + e.east) / 2], {
                content: e.label, permanent: true, direction: 'top', offset: [0, 4], className: 'sc-region-label', interactive: false
            }).addTo(m.layers);
        }

        for (const f of data.fleet) {
            const marker = L.circleMarker([f.lat, f.lon], {
                radius: 8,
                color: '#ffffff',
                weight: 2,
                fillColor: f.available ? '#4caf50' : '#f44336',
                fillOpacity: 1
            }).addTo(m.layers);
            marker.bindTooltip(f.label, { permanent: data.fleet.length <= 5, direction: 'right', offset: [8, 0] });
            bounds.push([f.lat, f.lon]);
        }

        if (!m.fitted && bounds.length > 0) {
            m.map.fitBounds(bounds, { padding: [30, 30], maxZoom: 8 });
            m.fitted = true;
        }
    }

    function fit(elementId) {
        const m = maps[elementId];
        if (m) m.fitted = false;
    }

    function setAircraft(elementId, lat, lon, label) {
        const m = maps[elementId];
        if (!m) return;
        if (lat === null) {
            if (m.live) { m.live.remove(); m.live = null; }
            return;
        }
        // Flight path: a point every ~0.2 nm, restarted after a jump (new flight, teleport), at most 2000 points.
        const here = L.latLng(lat, lon);
        if (!m.trail) m.trail = L.polyline([], { color: '#ffeb3b', weight: 2, opacity: 0.8, interactive: false }).addTo(m.map);
        const points = m.trail.getLatLngs();
        const last = points.length ? points[points.length - 1] : null;
        if (!last || last.distanceTo(here) > 50000) m.trail.setLatLngs([here]);
        else if (last.distanceTo(here) > 370) {
            points.push(here);
            if (points.length > 2000) points.shift();
            m.trail.setLatLngs(points);
        }
        if (!m.live) {
            m.live = L.circleMarker([lat, lon], { radius: 7, color: '#ffeb3b', weight: 3, fillColor: '#ff9800', fillOpacity: 1 }).addTo(m.map);
        }
        m.live.setLatLng([lat, lon]);
        m.live.bindTooltip(label);
    }

    function invalidate(elementId) {
        const m = maps[elementId];
        if (m) m.map.invalidateSize();
    }

    function dispose(elementId) {
        const m = maps[elementId];
        if (!m) return;
        m.map.remove();
        delete maps[elementId];
    }

    // Scrolls an element (e.g. a table row) into the middle of the view.
    function scrollToElement(id) {
        document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    return { init, update, fit, setAircraft, invalidate, dispose, scrollToElement };
})();
