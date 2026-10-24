// Exercise the standalone viewer script without a browser, network, or external dependencies.
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import assert from 'node:assert/strict';

const html = readFileSync(process.argv[2], 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
const original = html.match(/viewBox="([^"]+)"/)[1];
class Element {
  constructor() { this.events = {}; this.textContent = ''; this.attributes = {}; this.captured = false; this.clientWidth = 1200; this.clientHeight = 700; this.classList = { add() {}, remove() {} }; }
  addEventListener(name, fn) { this.events[name] = fn; }
  getAttribute(name) { return this.attributes[name]; }
  setAttribute(name, value) { this.attributes[name] = value; }
  setPointerCapture() { this.captured = true; }
  hasPointerCapture() { return this.captured; }
  releasePointerCapture() { this.captured = false; }
  focus() {}
  querySelector() { return svg; }
  getScreenCTM() { return { inverse() { return {}; } }; }
}
const ids = Object.fromEntries(['viewport', 'zoom-level', 'zoom-in', 'zoom-out', 'fit', 'actual', 'print'].map(id => [id, new Element()]));
const svg = new Element(); svg.attributes.viewBox = original;
const document = { events: {}, getElementById: id => ids[id], addEventListener(name, fn) { this.events[name] = fn; } };
let printed = 0;
const window = { events: {}, addEventListener(name, fn) { this.events[name] = fn; }, print() { printed++; this.events.beforeprint(); assert.equal(svg.attributes.viewBox, original); this.events.afterprint(); } };
class DOMPoint { constructor(x, y) { this.x = x; this.y = y; } matrixTransform() { return this; } }
runInNewContext(script, { document, window, DOMPoint }, { timeout: 1000 });
const box = () => svg.attributes.viewBox.split(' ').map(Number);
const click = id => ids[id].events.click();
const event = extra => ({ preventDefault() {}, target: { tagName: 'MAIN' }, ...extra });
const initial = box(); assert.ok(ids['zoom-level'].textContent.endsWith('%'));
click('zoom-in'); assert.ok(box()[2] < initial[2]);
click('zoom-out'); assert.ok(Math.abs(box()[2] - initial[2]) < 1e-8);
click('fit'); assert.equal(svg.attributes.viewBox, original);
click('actual'); assert.equal(box()[2], 1200); assert.equal(box()[3], 700);
ids.viewport.events.wheel(event({ deltaY: -100, clientX: 250, clientY: 200 })); assert.ok(box()[2] < 1200);
const beforeDrag = box();
ids.viewport.events.pointerdown(event({ button: 0, pointerId: 1, clientX: 100, clientY: 100 }));
ids.viewport.events.pointermove(event({ pointerId: 1, clientX: 180, clientY: 140 }));
assert.ok(Math.abs(box()[0] - beforeDrag[0] + 80) < 1e-8); assert.ok(Math.abs(box()[1] - beforeDrag[1] + 40) < 1e-8);
ids.viewport.events.pointerup(event({ pointerId: 1 })); assert.equal(ids.viewport.captured, false);
document.events.keydown(event({ key: '0' })); assert.equal(svg.attributes.viewBox, original);
document.events.keydown(event({ key: '+' })); assert.ok(box()[2] < initial[2]);
let prior = box()[0]; document.events.keydown(event({ key: 'ArrowRight' })); assert.ok(box()[0] > prior);
prior = svg.attributes.viewBox; document.events.keydown(event({ key: '0', ctrlKey: true })); assert.equal(svg.attributes.viewBox, prior);
click('print'); assert.equal(printed, 1); assert.equal(svg.attributes.viewBox, prior);
window.events.resize(); assert.equal(svg.attributes.viewBox, prior);
for (let i = 0; i < 200; i++) click('zoom-in'); assert.ok(box()[2] >= initial[2] / 100);
for (let i = 0; i < 200; i++) click('zoom-out'); assert.ok(box()[2] <= initial[2] * 4);
assert.ok(box().every(Number.isFinite));
console.log('PASS HTML viewer controls: zoom, fit, actual size, wheel, pan, pointer release, keyboard, print restoration, resize, and zoom limits.');
