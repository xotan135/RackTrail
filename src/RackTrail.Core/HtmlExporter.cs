using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace RackTrail.Core;

/// <summary>A portable browser viewer. All artwork and controls live in one offline file.</summary>
public static class HtmlExporter
{
    public static string Create(Project project,bool showConnectionLabels=false,CancellationToken cancellation=default)
    {
        string E(string text)=>WebUtility.HtmlEncode(text);
        var svg=VisualExporter.Svg(project,showConnectionLabels,cancellation)
            .Replace("<svg xmlns=","<svg role=\"img\" aria-label=\"Network map\" xmlns=",StringComparison.Ordinal);
        var hash=Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Script)));
        var html=new StringBuilder();
        html.Append("""
<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="referrer" content="no-referrer">
""");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'sha256-")
            .Append(hash).Append("'; base-uri 'none'; form-action 'none'\">\n<title>")
            .Append(E(project.Name)).Append(" — RackTrail</title>\n<style>").Append(Styles).Append("</style></head><body>\n<header><div class=\"heading\"><h1>")
            .Append(E(project.Name)).Append("</h1><p>")
            .Append(project.Nodes.Count(project.Visible)).Append(" visible items · ")
            .Append(project.Connections.Count(c=>project.VisibleEndpoint(c.From) is {} a&&project.VisibleEndpoint(c.To) is {} b&&a.Id!=b.Id))
            .Append(" connections · RackTrail browser view</p></div>")
            .Append("""
<nav aria-label="Map controls">
<button type="button" id="zoom-out" aria-label="Zoom out" title="Zoom out (-)">−</button>
<output id="zoom-level" aria-label="Current zoom">Fit</output>
<button type="button" id="zoom-in" aria-label="Zoom in" title="Zoom in (+)">+</button>
<button type="button" id="fit" title="Fit map (0)">Fit map</button>
<button type="button" id="actual">100%</button>
<button type="button" id="print">Print / PDF</button>
</nav></header>
<main id="viewport" tabindex="0" aria-label="Network map viewer. Drag to pan, scroll to zoom. Use plus, minus, zero, and arrow keys.">
""").Append(svg).Append("""
</main>
<footer>Drag to pan · Scroll to zoom · + / − to zoom · 0 to fit · Arrow keys to pan. This is a saved view; edit the original map in RackTrail.</footer>
<noscript><p class="notice">The map is visible below. Enable JavaScript to use zoom, pan, and print controls.</p></noscript>
<script>
""").Append(Script).Append("</script></body></html>");
        return html.ToString();
    }

    private const string Styles="""
:root{color-scheme:dark;font-family:system-ui,Segoe UI,sans-serif;background:#101722;color:#edf3fa}
*{box-sizing:border-box}body{margin:0;height:100vh;height:100dvh;display:grid;grid-template-rows:auto minmax(0,1fr) auto;overflow:hidden}
header{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap;padding:14px 20px;background:#172130;border-bottom:1px solid #34465e}
.heading{min-width:0;flex:1}h1{margin:0;font-size:18px;overflow-wrap:anywhere}p{margin:5px 0 0;font-size:12px;color:#b7c9e0}
nav{display:flex;flex-wrap:wrap;align-items:center;gap:7px}button{font:inherit;background:#24364c;color:#edf3fa;border:1px solid #506b8c;border-radius:6px;padding:7px 12px;cursor:pointer}
button:hover{background:#344e6c}button:focus-visible,main:focus-visible{outline:2px solid #e8c873;outline-offset:-2px}
output{font-variant-numeric:tabular-nums;min-width:55px;text-align:center;font-size:12px}
main{min-height:0;overflow:hidden;cursor:grab;touch-action:none}main.dragging{cursor:grabbing}main svg{display:block;width:100%;height:100%;user-select:none}
footer{padding:9px 20px;border-top:1px solid #34465e;color:#b7c9e0;font-size:12px;background:#172130}.notice{padding:12px}
@media(max-width:600px){header{padding:10px;gap:10px}footer{padding:8px 10px;font-size:11px}button{padding:7px 10px}}
@page{size:landscape;margin:10mm}
@media print{body{display:block;height:auto;overflow:visible;background:white}header{background:white;color:black;border:0;padding:0 0 8px}header p{color:#333}nav,footer,.notice{display:none}main{height:170mm;overflow:visible}main svg{width:100%;height:100%;print-color-adjust:exact;-webkit-print-color-adjust:exact}h1{font-size:16px}}
""";

    private const string Script="""
'use strict';
(() => {
  const viewport = document.getElementById('viewport');
  const svg = viewport.querySelector('svg');
  const original = svg.getAttribute('viewBox').trim().split(/\s+/).map(Number);
  let box = original.slice(), dragging = null, printBox = null;
  const apply = () => {
    svg.setAttribute('viewBox', box.join(' '));
    const scale = Math.min(viewport.clientWidth / box[2], viewport.clientHeight / box[3]);
    document.getElementById('zoom-level').textContent = Math.round(scale * 100) + '%';
  };
  const point = event => new DOMPoint(event.clientX, event.clientY).matrixTransform(svg.getScreenCTM().inverse());
  const fit = () => { box = original.slice(); apply(); };
  const zoom = (factor, center) => {
    center = center || { x: box[0] + box[2] / 2, y: box[1] + box[3] / 2 };
    const next = Math.max(original[2] / 100, Math.min(original[2] * 4, box[2] * factor));
    const ratio = next / box[2];
    box = [center.x - (center.x - box[0]) * ratio, center.y - (center.y - box[1]) * ratio, next, box[3] * ratio];
    apply();
  };
  document.getElementById('zoom-in').addEventListener('click', () => zoom(0.8));
  document.getElementById('zoom-out').addEventListener('click', () => zoom(1.25));
  document.getElementById('fit').addEventListener('click', fit);
  document.getElementById('actual').addEventListener('click', () => {
    const width = Math.max(1, viewport.clientWidth), height = Math.max(1, viewport.clientHeight);
    box = [box[0] + (box[2] - width) / 2, box[1] + (box[3] - height) / 2, width, height]; apply();
  });
  document.getElementById('print').addEventListener('click', () => window.print());
  window.addEventListener('beforeprint', () => { printBox = box.slice(); fit(); });
  window.addEventListener('afterprint', () => { if (printBox) { box = printBox; printBox = null; apply(); } });
  viewport.addEventListener('wheel', event => {
    event.preventDefault(); zoom(event.deltaY > 0 ? 1.15 : 1 / 1.15, point(event));
  }, { passive: false });
  viewport.addEventListener('pointerdown', event => {
    if (event.button !== 0 && event.button !== 1) return;
    event.preventDefault(); viewport.focus(); dragging = { id: event.pointerId, anchor: point(event) };
    viewport.setPointerCapture(event.pointerId); viewport.classList.add('dragging');
  });
  viewport.addEventListener('pointermove', event => {
    if (!dragging || event.pointerId !== dragging.id) return;
    const current = point(event); box[0] += dragging.anchor.x - current.x; box[1] += dragging.anchor.y - current.y; apply();
  });
  const stop = event => {
    if (!dragging || event.pointerId !== dragging.id) return;
    if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
    dragging = null; viewport.classList.remove('dragging');
  };
  viewport.addEventListener('pointerup', stop); viewport.addEventListener('pointercancel', stop);
  viewport.addEventListener('lostpointercapture', () => { dragging = null; viewport.classList.remove('dragging'); });
  document.addEventListener('keydown', event => {
    if (event.ctrlKey || event.metaKey || event.altKey || /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(event.target.tagName)) return;
    if (event.key === '+' || event.key === '=') zoom(0.8);
    else if (event.key === '-') zoom(1.25);
    else if (event.key === '0') fit();
    else if (event.key === 'ArrowLeft') { box[0] -= box[2] * 0.08; apply(); }
    else if (event.key === 'ArrowRight') { box[0] += box[2] * 0.08; apply(); }
    else if (event.key === 'ArrowUp') { box[1] -= box[3] * 0.08; apply(); }
    else if (event.key === 'ArrowDown') { box[1] += box[3] * 0.08; apply(); }
    else return;
    event.preventDefault();
  });
  window.addEventListener('resize', apply); fit();
})();
""";
}
