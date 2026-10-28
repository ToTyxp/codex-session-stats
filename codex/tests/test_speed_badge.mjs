import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../scripts/speed_badge.js", import.meta.url), "utf8");
const badgeSelector = "[data-codex-live-speed-badge]";
const metricTooltip = "最近一次已完成模型调用均速（含推理与首 token 等待）";

// A small deterministic renderer fixture: it supplies DOM identity, layout
// bounds, observers, a frame clock, and events, without installing dependencies.
// The installed IIFE is exercised through its public API and rendered DOM.
function fixture({ reducedMotion = false, wrapper = false, attachmentHeight = 48 } = {}) {
  let clock = 0;
  let nextId = 1;
  const frames = new Map();
  const intervals = new Map();
  const observers = new Set();
  const resizeObservers = new Set();

  class EventTarget {
    listeners = new Map();
    addEventListener(type, callback) {
      if (!this.listeners.has(type)) this.listeners.set(type, new Set());
      this.listeners.get(type).add(callback);
    }
    removeEventListener(type, callback) { this.listeners.get(type)?.delete(callback); }
    dispatchEvent(event) {
      for (const callback of [...(this.listeners.get(event.type) || [])]) callback(event);
    }
    listenerCount() { return [...this.listeners.values()].reduce((sum, set) => sum + set.size, 0); }
  }

  class Element extends EventTarget {
    nodeType = 1;
    children = [];
    parentElement = null;
    attributes = new Map();
    style = {};
    hidden = false;
    _text = "";
    bounds = { width: 100, height: 30, left: 0, top: 0 };
    css = { display: "block", flexDirection: "row", position: "static" };
    constructor(tag) { super(); this.tagName = tag.toUpperCase(); }
    focus() {
      document.activeElement = this;
      document.dispatchEvent({ type: "focusin", target: this });
    }
    get isConnected() { return this === document.documentElement || !!this.parentElement?.isConnected; }
    get nextSibling() {
      return this.parentElement?.children[this.parentElement.children.indexOf(this) + 1] || null;
    }
    get id() { return this.getAttribute("id"); }
    set id(value) { this.setAttribute("id", value); }
    setAttribute(key, value) {
      const old = this.getAttribute(key);
      this.attributes.set(key, String(value));
      if (old !== String(value)) notify({ type: "attributes", target: this, attributeName: key });
    }
    getAttribute(key) { return this.attributes.get(key) ?? null; }
    removeAttribute(key) {
      this.attributes.delete(key);
      notify({ type: "attributes", target: this, attributeName: key });
    }
    appendChild(node) { return this.insertBefore(node, null); }
    insertBefore(node, before) {
      if (node.parentElement) node.remove();
      const index = before ? this.children.indexOf(before) : this.children.length;
      assert.ok(index >= 0, "insertion reference belongs to its parent");
      this.children.splice(index, 0, node);
      node.parentElement = this;
      notify({ type: "childList", target: this, addedNodes: [node], removedNodes: [] });
      return node;
    }
    remove() {
      const previous = this.parentElement;
      if (!previous) return;
      previous.children.splice(previous.children.indexOf(this), 1);
      this.parentElement = null;
      notify({ type: "childList", target: previous, addedNodes: [], removedNodes: [this] });
    }
    contains(node) { return this === node || this.children.some(child => child.contains(node)); }
    closest(selector) {
      for (let node = this; node; node = node.parentElement) if (node.matches(selector)) return node;
      return null;
    }
    matches(selector) {
      return selector.split(",").some(part => {
        const negatives = [...part.matchAll(/:not\(([^)]+)\)/g)].map(match => match[1]);
        if (negatives.some(negative => this.matches(negative))) return false;
        part = part.replace(/:not\([^)]+\)/g, "");
        const attrs = [...part.matchAll(/\[([^\]=]+)(?:="([^"]*)")?\]/g)];
        return attrs.length > 0 && attrs.every(([, key, value]) =>
          value === undefined ? this.attributes.has(key) : this.getAttribute(key) === value);
      });
    }
    querySelectorAll(selector) {
      return this.children.flatMap(child => [
        ...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector),
      ]);
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    get textContent() { return this._text + this.children.map(child => child.textContent).join(""); }
    set textContent(value) {
      this._text = String(value);
      this.children = [];
      notify({ type: "childList", target: this, addedNodes: [{ nodeType: 3 }], removedNodes: [] });
    }
    getClientRects() {
      for (let node = this; node; node = node.parentElement) {
        if (node.hidden || node.css.display === "none") return [];
      }
      return this.isConnected ? [this.getBoundingClientRect()] : [];
    }
    getBoundingClientRect() {
      const { width, height, left, top } = this.bounds;
      // Apply the injected stylesheet's fixed slot width, rather than deriving
      // layout width from the current number's string length.
      const sheet = document.head.children.find(child => child.tagName === "STYLE")?._text || "";
      const fixedWidth = this.matches(badgeSelector) ? this.parentElement?.bounds.width : 0;
      return { width: this.hidden ? 0 : fixedWidth || width, height, left, top,
        right: left + width, bottom: top + height };
    }
  }

  const document = new EventTarget();
  document.visibilityState = "visible";
  document.documentElement = new Element("html");
  document.head = new Element("head");
  document.body = new Element("body");
  document.body.bounds.height = 300;
  document.documentElement.appendChild(document.head);
  document.documentElement.appendChild(document.body);
  document.createElement = tag => new Element(tag);
  document.createElementNS = (_ns, tag) => new Element(tag);
  document.querySelectorAll = selector => document.documentElement.querySelectorAll(selector);
  document.querySelector = selector => document.querySelectorAll(selector)[0] || null;

  function notify(record) {
    for (const observer of observers) {
      if (!observer.target?.contains(record.target)) continue;
      if (record.type === "attributes" && !observer.options.attributeFilter.includes(record.attributeName)) continue;
      observer.callback([record]);
    }
  }
  class MutationObserver {
    constructor(callback) { this.callback = callback; observers.add(this); }
    observe(target, options) { this.target = target; this.options = options; }
    disconnect() { this.target = null; }
  }
  class ResizeObserver {
    nodes = new Set();
    constructor(callback) { this.callback = callback; resizeObservers.add(this); }
    observe(node) { this.nodes.add(node); }
    disconnect() { this.nodes.clear(); }
  }
  const media = new EventTarget();
  media.matches = reducedMotion;
  const window = new EventTarget();
  Object.assign(window, {
    innerWidth: 900, innerHeight: 800, window, document, performance: { now: () => clock }, MutationObserver, ResizeObserver,
    getComputedStyle(node) {
      return { ...node.css, ...node.style, display: node.hidden ? "none" : node.css.display };
    },
    matchMedia: () => media,
    requestAnimationFrame(callback) { const id = nextId++; frames.set(id, callback); return id; },
    cancelAnimationFrame(id) { frames.delete(id); },
    setInterval(callback, duration) {
      const id = nextId++;
      intervals.set(id, { callback, duration, at: clock + duration });
      return id;
    },
    clearInterval(id) { intervals.delete(id); },
  });

  const row = new Element("div");
  row.setAttribute("data-app-action-sidebar-thread-row", "");
  row.setAttribute("data-app-action-sidebar-thread-active", "true");
  row.setAttribute("data-app-action-sidebar-thread-id", "thread-a");
  row.setAttribute("data-app-action-sidebar-thread-host-id", "local");
  document.body.appendChild(row);
  const composer = new Element("div");
  composer.setAttribute("data-above-composer-portal", "true");
  composer.setAttribute("data-above-composer-conversation-id", "thread-a");
  document.body.appendChild(composer);
  const toolbar = new Element("div");
  toolbar.css.display = "flex";
  toolbar.bounds = { width: 800, height: 40, top: 0, left: 0 };
  document.body.appendChild(toolbar);
  const tools = new Element("button");
  tools.bounds.width = 48;
  toolbar.appendChild(tools);
  const circleWrapper = new Element("span");
  circleWrapper.css.display = "inline";
  circleWrapper.bounds.width = 18;
  const circle = new Element("span");
  circle.css.display = "inline-flex";
  circle.bounds.width = 12;
  circle.setAttribute("role", "img");
  circle.setAttribute("aria-label", "Context usage: 20%");
  circleWrapper.appendChild(circle);
  toolbar.appendChild(circleWrapper);
  const anchor = new Element("button");
  anchor.bounds.width = 100;
  anchor.setAttribute("data-composer-navigation-target", "permissions");
  anchor.textContent = "帮我批准";
  if (wrapper) {
    const span = new Element("span");
    span.bounds.width = 100;
    toolbar.appendChild(span);
    span.appendChild(anchor);
  } else toolbar.appendChild(anchor);
  const model = new Element("button");
  model.bounds.width = 180;
  model.setAttribute("data-codex-intelligence-trigger", "true");
  model.setAttribute("data-composer-navigation-target", "reasoning");
  model.textContent = "GPT-6 High";
  toolbar.appendChild(model);
  const send = new Element("button");
  send.bounds.width = 40;
  toolbar.appendChild(send);

  const layout = nativeFooterGrid({ document, toolbar, anchor, model, circleWrapper }, { attachmentHeight });
  const { grid } = layout;
  const outer = new Element("div");
  outer.css.overflow = "visible";
  outer.bounds = {width:800,height:120,left:0,top:0};
  const box = new Element("div");
  box.setAttribute("role", "presentation");
  Object.assign(box.css,{borderRadius:"22px",display:"flex",flexDirection:"column",overflow:"auto"});
  document.body.insertBefore(outer,grid); outer.appendChild(box); box.appendChild(grid);
  layout.outer=outer; layout.box=box;
  const context = vm.createContext(window);
  const install = () => vm.runInContext(source, context);
  install();
  function advance(duration) {
    const until = clock + duration;
    do {
      const intervalAt = Math.min(...[...intervals.values()].map(timer => timer.at));
      clock = Math.min(clock + 16, until, intervalAt);
      for (const timer of [...intervals.values()]) {
        if (timer.at <= clock) { timer.at += timer.duration; timer.callback(); }
      }
      const ready = [...frames.values()];
      frames.clear();
      for (const callback of ready) callback(clock);
    } while (clock < until);
  }
  return {
    window, document, row, composer, toolbar, anchor, model, circle, circleWrapper, media, install, advance, grid, layout,
    api: () => window.__codexLiveSpeedBadge,
    badge: () => document.querySelector(badgeSelector),
    update(rate, extra = {}) {
      return window.__codexLiveSpeedBadge.update({
        threadId: "thread-a", hostId: "local", status: "connected", phase: "completed",
        source: "agent-speed/1.4.1", metric: "response-wall", rate, revision: 1, ...extra,
      });
    },
    changeMotion(value) { media.matches = value; media.dispatchEvent({ type: "change", matches: value }); },
    resources() {
      return { frames: frames.size, timers: intervals.size,
        mutationObservers: [...observers].filter(observer => observer.target).length,
        resizeObservers: [...resizeObservers].filter(observer => observer.nodes.size).length,
        listeners: window.listenerCount() + document.listenerCount() + media.listenerCount() };
    },
  };
}

function nativeFooterGrid(f, { attachmentHeight = 48, rightWidth = 468.719 } = {}) {
  const make = (display, width, height = 28) => {
    const node = f.document.createElement("div");
    node.css.display = display;
    Object.assign(node.bounds, { width, height });
    return node;
  };
  const grid = make("grid", 119 + rightWidth + 26, attachmentHeight + 28);
  Object.assign(grid.css, { gridTemplateColumns: `119px 0px ${rightWidth}px`,
    gridTemplateRows: `${attachmentHeight}px 28px`, columnGap: "5px", paddingLeft: "8px", paddingRight: "8px" });
  const attachment = make("block", grid.bounds.width - 16, attachmentHeight);
  Object.assign(attachment.css, { gridRowStart: "1", gridColumnStart: "1", gridColumnEnd: "-1" });
  const left = make("block", 119);
  Object.assign(left.css, { gridRowStart: "2", gridColumnStart: "1" });
  const right = make("block", rightWidth);
  Object.assign(right.css, { gridRowStart: "2", gridColumnStart: "3", minWidth: "0px" });
  f.document.body.insertBefore(grid, f.toolbar);
  grid.appendChild(attachment);
  grid.appendChild(left);
  grid.appendChild(right);
  left.appendChild(f.toolbar);
  f.toolbar.css.columnGap = "5px";
  Object.assign(f.toolbar.bounds, { width: 119, height: 28 });
  const tools = f.toolbar.children[0];
  tools.bounds.width = 28;
  const contentsTools = make("contents", 0);
  f.toolbar.insertBefore(contentsTools, tools);
  contentsTools.appendChild(tools);
  f.anchor.bounds.width = 86;
  const send = f.toolbar.children.at(-1);
  const contentsRight = make("contents", 0);
  const rightRow = make("flex", rightWidth);
  rightRow.css.justifyContent = "flex-end";
  const growing = make("flex", rightWidth - 64);
  Object.assign(growing.css, { flexGrow: "1", flexShrink: "1", flexBasis: "0%", minWidth: "0px", justifyContent: "flex-end" });
  const modelOuter = make("flex", 161.695);
  const modelInner = make("flex", 161.695);
  modelInner.css.columnGap = "4px";
  f.circleWrapper.bounds.width = 16;
  f.model.bounds.width = 141.695;
  const fixed = make("flex", 64);
  Object.assign(fixed.css, { flexGrow: "0", flexShrink: "0", flexBasis: "auto" });
  send.bounds.width = 64;
  right.appendChild(contentsRight);
  contentsRight.appendChild(rightRow);
  rightRow.appendChild(growing);
  rightRow.appendChild(fixed);
  growing.appendChild(modelOuter);
  modelOuter.appendChild(modelInner);
  modelInner.appendChild(f.circleWrapper);
  modelInner.appendChild(f.model);
  fixed.appendChild(send);
  return { grid, right, rightRow, growing, tools, send };
}

test("unknown starts honestly outside the input and native controls remain intact", () => {
  const f = fixture();
  const badge = f.badge();
  assert.equal(badge.textContent, "— tok/s");
  assert.equal(badge.hidden, false);
  assert.equal(f.api().inspect().rate, null);
  const width = badge.getBoundingClientRect().width;
  assert.equal(width, 800);
  let clicks = 0;
  let keys = 0;
  f.anchor.addEventListener("click", () => clicks++);
  f.anchor.addEventListener("keydown", () => keys++);
  f.update(123);
  assert.equal(badge.textContent, "123 tok/s", "first data does not animate from fabricated zero");
  f.update(9999);
  f.advance(200);
  assert.equal(badge.textContent, "9999 tok/s");
  f.update(null);
  assert.equal(badge.textContent, "— tok/s");
  assert.equal(f.badge(), badge);
  assert.equal(badge.getBoundingClientRect().width, width);
  assert.equal(badge.parentElement, f.layout.outer);
  assert.equal(f.toolbar.contains(badge), false);
  assert.equal(f.anchor.textContent, "帮我批准");
  assert.equal(f.model.textContent, "GPT-6 High");
  f.anchor.dispatchEvent({ type: "click" });
  f.anchor.dispatchEvent({ type: "keydown", key: "Enter" });
  assert.equal(clicks, 1);
  assert.equal(keys, 1);
  f.api().destroy();
});

test("a new target continues from the number currently displayed", () => {
  const f = fixture();
  f.update(100);
  f.advance(130);
  f.update(200);
  f.advance(90);
  const midway = f.api().inspect().displayedRate;
  assert.ok(midway > 100 && midway < 200);
  f.update(400);
  assert.equal(f.api().inspect().displayedRate, midway, "retarget does not jump to the previous target");
  f.advance(90);
  assert.ok(f.api().inspect().displayedRate > midway && f.api().inspect().displayedRate < 400);
  f.advance(90);
  assert.equal(f.badge().textContent, "400 tok/s");
  assert.equal(f.api().inspect().animating, false);
  f.api().destroy();
});

test("disconnect, invalid values, and unknown fade without repeatedly restarting the fade", () => {
  const f = fixture();
  f.update(88);
  f.advance(150);
  f.update(999, { status: "disconnected" });
  assert.equal(f.badge().textContent, "— tok/s");
  assert.equal(f.api().inspect().rate, null);
  f.advance(60);
  const opacity = Number(f.badge().querySelector("[data-speed-label]").style.opacity);
  f.update(null);
  assert.equal(Number(f.badge().querySelector("[data-speed-label]").style.opacity), opacity);
  f.advance(60);
  assert.equal(Number(f.badge().querySelector("[data-speed-label]").style.opacity), 1);
  for (const invalid of [NaN, Infinity, -1, "123"]) {
    f.update(invalid);
    assert.equal(f.badge().textContent, "— tok/s");
  }
  f.update(0);
  assert.equal(f.badge().textContent, "0 tok/s", "a measured zero is valid");
  f.update(123, { status: "incompatible" });
  assert.equal(f.badge().textContent, "— tok/s");
  f.api().destroy();
});

test("reduced motion applies immediately and responds to preference changes", () => {
  const f = fixture({ reducedMotion: true });
  f.update(50);
  assert.equal(f.resources().frames, 0);
  f.update(150);
  assert.equal(f.badge().textContent, "150 tok/s");
  assert.equal(f.resources().frames, 0);
  f.changeMotion(false);
  f.update(300);
  f.advance(60);
  assert.ok(f.api().inspect().displayedRate < 300);
  f.changeMotion(true);
  assert.equal(f.badge().textContent, "300 tok/s");
  assert.equal(f.api().inspect().animating, false);
  assert.equal(f.resources().frames, 0);
  f.update(null);
  assert.equal(Number(f.badge().querySelector("[data-speed-label]").style.opacity), 1);
  f.api().destroy();
});

test("switching conversations clears first, rejects old updates, and isolates hosts", () => {
  const f = fixture();
  f.update(123);
  f.composer.setAttribute("data-above-composer-conversation-id", "thread-b");
  f.advance(16);
  assert.equal(f.badge().hidden, false, "the current composer keeps an honest unknown placeholder");
  assert.equal(f.badge().textContent, "— tok/s");
  f.update(456);
  assert.equal(f.api().inspect().rate, null);
  f.row.setAttribute("data-app-action-sidebar-thread-id", "thread-b");
  f.advance(16);
  assert.equal(f.badge().hidden, false);
  assert.equal(f.badge().textContent, "— tok/s");
  f.update(90, { threadId: "thread-b" });
  assert.equal(f.badge().textContent, "90 tok/s");
  f.update(900);
  assert.equal(f.badge().textContent, "90 tok/s");
  f.row.setAttribute("data-app-action-sidebar-thread-host-id", "remote-machine");
  f.advance(16);
  assert.equal(f.badge().textContent, "— tok/s");
  f.update(999, { threadId: "thread-b" });
  assert.equal(f.api().inspect().rate, null);
  f.update(44, { threadId: "thread-b", hostId: "remote-machine" });
  assert.equal(f.badge().textContent, "44 tok/s");
  f.api().destroy();
});

test("heartbeats keep a rate alive, a stopped source expires, and recovery needs fresh data", () => {
  const f = fixture();
  f.update(123);
  f.advance(1000);
  f.update(123);
  f.advance(1000);
  assert.equal(f.badge().textContent, "123 tok/s", "same revision remains a live heartbeat");
  f.advance(800);
  assert.equal(f.badge().textContent, "— tok/s");
  assert.equal(f.api().inspect().stale, true);
  f.update(321);
  assert.equal(f.badge().textContent, "321 tok/s");
  assert.equal(f.api().inspect().stale, false);
  f.api().destroy();
});

test("completed samples expose the backend metric and metadata without recomputing token arithmetic", () => {
  const f = fixture();
  const sample = {
    turnId: "turn-1", responseId: "response-1", model: "gpt-6.1-sol",
    outputTokens: 250, reasoningTokens: 150, durationMs: 10000, completedAtMs: 1793073600000,
  };
  f.update(25, sample);
  assert.equal(f.badge().textContent, "25 tok/s", "output already contains reasoning; it must not be added again");
  const result = f.api().inspect();
  for (const [key, value] of Object.entries(sample)) assert.equal(result[key], value);
  assert.equal(result.source, "agent-speed/1.4.1");
  assert.equal(result.metric, "response-wall");
  assert.equal(result.phase, "completed");
  assert.equal(result.retained, false);
  assert.ok(f.badge().title.startsWith(metricTooltip));
  assert.ok(f.badge().title.includes(sample.model));
  assert.ok(f.badge().title.includes("完成于"));
  assert.equal(f.badge().title.includes("上次有效结果"), false);
  f.update(41, { ...sample, responseId: "response-2" });
  f.advance(180);
  assert.equal(f.badge().textContent, "41 tok/s", "the renderer consumes rate, even when metadata cannot derive it");
  f.api().destroy();
});

test("a next turn keeps the backend's last completed sample; a new response retargets smoothly", () => {
  const f = fixture();
  f.update(100, { turnId: "turn-1", responseId: "response-1" });
  f.advance(200);
  f.update(100, { turnId: "turn-2", responseId: "response-1", retained: true });
  assert.equal(f.badge().textContent, "100 tok/s");
  assert.equal(f.api().inspect().turnId, "turn-2");
  assert.equal(f.api().inspect().responseId, "response-1");
  assert.equal(f.api().inspect().animating, false, "a new turn cannot clear or restart the last result");
  f.update(200, { turnId: "turn-2", responseId: "response-2", retained: false });
  assert.equal(f.api().inspect().displayedRate, 100);
  assert.equal(f.api().inspect().responseId, "response-2");
  assert.equal(f.api().inspect().retained, false);
  assert.equal(f.badge().title, metricTooltip);
  f.advance(90);
  assert.ok(f.api().inspect().displayedRate > 100 && f.api().inspect().displayedRate < 200);
  f.advance(90);
  assert.equal(f.badge().textContent, "200 tok/s");
  f.api().destroy();
});

test("cached heartbeats preserve the last result through backend disconnect; a stopped bridge still expires", () => {
  const f = fixture();
  const sample = { turnId: "turn-1", responseId: "response-1", retained: true, status: "cached" };
  for (let heartbeat = 0; heartbeat < 3; heartbeat += 1) {
    f.update(123, sample);
    assert.equal(f.badge().textContent, "123 tok/s");
    assert.equal(f.api().inspect().status, "cached");
    assert.equal(f.api().inspect().phase, "completed");
    assert.equal(f.api().inspect().retained, true);
    assert.equal(f.badge().title, `${metricTooltip} · 上次有效结果`);
    f.advance(1000);
  }
  f.advance(1800);
  assert.equal(f.badge().textContent, "— tok/s");
  assert.equal(f.api().inspect().stale, true);
  assert.equal(f.api().inspect().retained, false);
  assert.equal(f.api().inspect().turnId, null);
  assert.equal(f.api().inspect().responseId, null);
  assert.equal(f.badge().title, metricTooltip);
  f.update(123, sample);
  assert.equal(f.badge().textContent, "123 tok/s");
  assert.equal(f.api().inspect().stale, false);
  f.api().destroy();
});

test("A/B/A restores each backend cached sample only after matching the selected conversation", () => {
  const f = fixture();
  const a = { threadId: "thread-a", responseId: "response-a", model: "model-a", completedAtMs: 1793073600000 };
  const b = { threadId: "thread-b", responseId: "response-b", model: "model-b", completedAtMs: 1793073601000 };
  f.update(123, a);
  f.composer.setAttribute("data-above-composer-conversation-id", "thread-b");
  f.row.setAttribute("data-app-action-sidebar-thread-id", "thread-b");
  f.api().selection();
  assert.equal(f.badge().textContent, "— tok/s", "no frontend cache can leak the previous conversation");
  f.update(123, { ...a, status: "cached", retained: true });
  assert.equal(f.api().inspect().rate, null, "late A heartbeat cannot populate B");
  f.update(55, { ...b, status: "cached", retained: true });
  assert.equal(f.badge().textContent, "55 tok/s");
  assert.equal(f.api().inspect().responseId, "response-b");
  f.composer.setAttribute("data-above-composer-conversation-id", "thread-a");
  f.row.setAttribute("data-app-action-sidebar-thread-id", "thread-a");
  f.api().selection();
  assert.equal(f.badge().textContent, "— tok/s");
  f.update(55, { ...b, status: "cached", retained: true });
  assert.equal(f.api().inspect().rate, null, "late B heartbeat cannot populate A");
  f.update(123, { ...a, status: "cached", retained: true });
  assert.equal(f.badge().textContent, "123 tok/s");
  assert.equal(f.api().inspect().responseId, "response-a");
  assert.ok(f.badge().title.includes("model-a"));
  assert.equal(f.badge().title.includes("model-b"), false);
  f.api().destroy();
});

test("a conversation with no completed sample stays unknown instead of inventing zero", () => {
  const f = fixture();
  f.update(null, { status: "cached", responseId: null, retained: false });
  assert.equal(f.api().inspect().rate, null);
  assert.equal(f.api().inspect().responseId, null);
  assert.equal(f.api().inspect().retained, false);
  assert.equal(f.badge().textContent, "— tok/s");
  assert.equal(f.badge().title, metricTooltip);
  f.api().destroy();
});

test("retained inactive app pages cannot supply the current thread or mount anchor", () => {
  const f = fixture();
  const shell = f.document.createElement("div");
  shell.setAttribute("data-app-shell-active-page", "false");
  const staleRow = f.document.createElement("div");
  staleRow.setAttribute("data-app-action-sidebar-thread-row", "");
  staleRow.setAttribute("data-app-action-sidebar-thread-active", "true");
  staleRow.setAttribute("data-app-action-sidebar-thread-id", "old-thread");
  shell.appendChild(staleRow);
  const staleComposer = f.document.createElement("div");
  staleComposer.setAttribute("data-above-composer-conversation-id", "old-thread");
  shell.appendChild(staleComposer);
  const staleToolbar = f.document.createElement("div");
  staleToolbar.css.display = "flex";
  const staleAnchor = f.document.createElement("button");
  staleAnchor.setAttribute("data-composer-navigation-target", "permissions");
  staleToolbar.appendChild(staleAnchor);
  shell.appendChild(staleToolbar);
  f.document.body.insertBefore(shell, f.row);
  f.advance(16);
  f.update(123);
  assert.equal(f.api().inspect().activeThreadId, "thread-a");
  assert.equal(f.badge().parentElement, f.layout.outer);
  assert.equal(f.badge().textContent, "123 tok/s");
  f.update(999, { threadId: "old-thread" });
  assert.equal(f.badge().textContent, "123 tok/s");
  f.api().destroy();
});

test("the native local sidebar key and bare composer conversation ID identify one thread", () => {
  const f = fixture();
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:thread-a");
  f.advance(16);
  f.update(123);
  assert.equal(f.api().inspect().activeThreadId, "thread-a");
  assert.equal(f.badge().hidden, false);
  assert.equal(f.badge().textContent, "123 tok/s");
  f.update(333, { threadId: "local:thread-a" });
  f.advance(180);
  assert.equal(f.badge().textContent, "333 tok/s");
  f.api().destroy();
});

test("external placement preserves native children and leaves the rounded border unchanged", () => {
  const f = fixture(); f.api().destroy();
  const native = [...f.grid.children], style = {...f.layout.box.style};
  f.install();
  assert.deepEqual(f.grid.children,native);
  assert.deepEqual(f.layout.box.style,style);
  assert.deepEqual(f.layout.outer.children,[f.layout.box,f.badge()]);
  assert.equal(f.layout.box.contains(f.badge()),false);
  f.layout.growing.bounds.width=50; f.update(123);
  assert.equal(f.badge().hidden,false,"model width no longer controls external strip");
  f.api().destroy(); assert.deepEqual(f.layout.outer.children,[f.layout.box]);
});

test("Plan and Goal stay in the native row when mounted and when controls are added dynamically", () => {
  const f = fixture();
  const plan = f.document.createElement("button");
  const goal = f.document.createElement("button");
  plan.textContent = "Plan";
  goal.textContent = "Goal";
  plan.bounds.width = 52;
  goal.bounds.width = 56;
  f.toolbar.appendChild(plan);
  f.api().destroy();
  const nativeChildren = [...f.toolbar.children];
  f.install();
  assert.deepEqual(f.toolbar.children, nativeChildren);
  const badge = f.badge();
  f.toolbar.appendChild(goal);
  f.update(123);
  assert.deepEqual(f.toolbar.children, [...nativeChildren, goal]);
  assert.equal(badge.parentElement, f.layout.outer);
  assert.equal(f.toolbar.contains(badge), false);
  assert.equal(plan.getBoundingClientRect().width, 52);
  assert.equal(goal.getBoundingClientRect().width, 56);
  assert.equal(f.anchor.getBoundingClientRect().width, 86);
  assert.equal(f.model.getBoundingClientRect().width, 141.695);
  let hiddenWrites = 0;
  let hidden = badge.hidden;
  Object.defineProperty(badge, "hidden", {
    get: () => hidden, set: value => { hiddenWrites++; hidden = value; },
  });
  f.update(123);
  f.update(123);
  assert.equal(hiddenWrites, 0, "ordinary heartbeats never temporarily remove the metric");
  f.api().destroy();
});

test("unknown or clipped outer containers do not mount inside native input", () => {
  const f=fixture(); f.layout.box.removeAttribute("role"); f.update(123);
  assert.equal(f.badge(),null);
  f.layout.box.setAttribute("role","presentation"); f.update(123);
  assert.equal(f.badge().parentElement,f.layout.outer);
  f.layout.outer.css.overflow="hidden"; f.update(123); assert.equal(f.badge(),null);
  f.api().destroy();
});

test("attachment heights do not affect external metric placement", () => {
  for (const attachmentHeight of [144, 0]) {
    const f = fixture({ attachmentHeight });
    f.update(123);
    assert.equal(f.badge().hidden, false);
    assert.equal(f.badge().parentElement, f.layout.outer);
    assert.equal(f.badge().textContent, "123 tok/s");
    f.api().destroy();
  }
});

test("tooltip wrappers, DOM replacement, and non-chat navigation preserve the right lifetime", () => {
  const f = fixture({ wrapper: true });
  f.update(123);
  const previous = f.badge();
  const wrapper = f.anchor.parentElement;
  assert.equal(previous.parentElement, f.layout.outer);
  wrapper.remove();
  f.advance(16);
  assert.equal(f.badge(), null);
  assert.equal(f.api().inspect().rate, null);
  f.toolbar.appendChild(wrapper);
  f.advance(16);
  assert.notEqual(f.badge(), previous);
  assert.equal(f.badge().textContent, "— tok/s");
  assert.equal(previous.isConnected, false);
  f.update(44);
  assert.equal(f.badge().textContent, "44 tok/s");
  f.document.visibilityState = "hidden";
  f.document.dispatchEvent({ type: "visibilitychange" });
  f.advance(16);
  assert.equal(f.badge().hidden, true);
  f.document.visibilityState = "visible";
  f.document.dispatchEvent({ type: "visibilitychange" });
  f.advance(16);
  assert.equal(f.badge().textContent, "— tok/s");
  f.api().destroy();
});

test("permissions tooltip wrappers identify the footer without receiving the metric", () => {
  const f = fixture({ wrapper: true });
  const wrapper = f.anchor.parentElement;
  wrapper.css.display = "inline-flex";
  f.circleWrapper.remove();
  f.update(123);
  assert.equal(f.badge().parentElement, f.layout.outer);
  assert.equal(wrapper.contains(f.badge()), false);
  assert.equal(f.badge().textContent, "123 tok/s");
  assert.equal(f.anchor.textContent, "帮我批准");
  assert.equal(f.model.textContent, "GPT-6 High");
  f.api().destroy();
});

test("a new composer shows unknown without reusing the old sidebar ID, then follows the created thread", () => {
  const f = fixture();
  f.update(123);
  f.composer.setAttribute("data-above-composer-conversation-id", "");
  let selection = f.api().selection();
  assert.equal(selection.threadId, null);
  assert.equal(selection.hostId, "local");
  assert.equal(f.badge().hidden, false);
  assert.equal(f.badge().textContent, "— tok/s");
  f.composer.removeAttribute("data-above-composer-conversation-id");
  assert.equal(f.api().selection().threadId, null, "the native empty portal can omit its conversation-id attribute");
  assert.equal(f.badge().hidden, false);
  f.update(999);
  assert.equal(f.api().inspect().rate, null, "previous conversation data cannot populate the new composer");
  f.composer.setAttribute("data-above-composer-conversation-id", "new-thread");
  assert.equal(f.api().selection().threadId, "new-thread", "the real composer ID is authoritative before sidebar replacement");
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:new-thread");
  selection = f.api().selection();
  assert.equal(selection.threadId, "new-thread");
  assert.equal(selection.hostId, "local");
  f.update(87, { threadId: "new-thread" });
  assert.equal(f.badge().textContent, "87 tok/s");
  f.api().destroy();
});

test("a real composer ID outranks placeholder or stale sidebar IDs without inheriting their remote host", () => {
  const f = fixture();
  const realId = "thread-created";
  f.composer.setAttribute("data-above-composer-conversation-id", realId);
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:client-new-thread:pending-1");
  f.row.setAttribute("data-app-action-sidebar-thread-host-id", "unrelated-remote");
  assert.equal(f.api().selection().threadId, realId);
  assert.equal(f.api().selection().hostId, "local");
  f.update(123, { threadId: realId });
  assert.equal(f.badge().textContent, "123 tok/s");
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:previous-thread");
  assert.equal(f.api().selection().threadId, realId);
  assert.equal(f.api().selection().hostId, "local");
  assert.equal(f.badge().textContent, "123 tok/s");
  f.row.setAttribute("data-app-action-sidebar-thread-id", `local:${realId}`);
  assert.equal(f.api().selection().hostId, "unrelated-remote", "only a matching row may supply the host");
  f.composer.setAttribute("data-above-composer-host-id", "composer-host");
  assert.equal(f.api().selection().hostId, "composer-host");
  f.composer.remove();
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:client-new-thread:pending");
  assert.equal(f.api().selection().threadId, null, "a sidebar-only client placeholder is never a subscription ID");
  assert.equal(f.badge().textContent, "— tok/s");
  f.api().destroy();
});

test("an anonymous portal before the bound composer cannot erase the actual conversation identity", () => {
  const f = fixture();
  const anonymousPortal = f.document.createElement("div");
  anonymousPortal.setAttribute("data-above-composer-portal", "true");
  f.document.body.insertBefore(anonymousPortal, f.composer);
  assert.equal(f.api().selection().threadId, "thread-a");
  f.update(123);
  assert.equal(f.api().inspect().activeThreadId, "thread-a");
  assert.equal(f.api().inspect().phase, "completed");
  assert.equal(f.badge().textContent, "123 tok/s");
  f.composer.setAttribute("data-above-composer-conversation-id", "");
  assert.equal(f.api().selection().threadId, null, "an explicit empty identity still means pending");
  assert.equal(f.badge().textContent, "— tok/s");
  f.api().destroy();
});

test("a CSS-hidden retained composer cannot supply identity to the visible blank composer", () => {
  const f = fixture();
  f.update(123);
  const retainedPage = f.document.createElement("div");
  retainedPage.css.display = "none";
  f.document.body.insertBefore(retainedPage, f.composer);
  retainedPage.appendChild(f.composer);
  const blankComposer = f.document.createElement("div");
  blankComposer.setAttribute("data-above-composer-portal", "true");
  f.document.body.appendChild(blankComposer);

  assert.equal(f.api().selection().threadId, null);
  assert.equal(f.badge().textContent, "— tok/s");
  f.update(999);
  assert.equal(f.api().inspect().rate, null, "an old thread heartbeat cannot populate the blank composer");

  blankComposer.setAttribute("data-above-composer-conversation-id", "new-thread");
  assert.equal(f.api().selection().threadId, "new-thread");
  f.update(87, { threadId: "new-thread" });
  assert.equal(f.badge().textContent, "87 tok/s");
  f.api().destroy();
});

test("a hidden identity marker in the visible permissions page outranks a pending sidebar", () => {
  const f = fixture();
  const realId = "thread-hidden-marker";
  const page = f.document.createElement("div");
  page.setAttribute("data-app-shell-active-page", "true");
  page.css.display = "contents";
  f.document.body.appendChild(page);
  page.appendChild(f.composer);
  page.appendChild(f.layout.outer);
  f.composer.css.display = "none";
  f.composer.setAttribute("data-above-composer-conversation-id", realId);
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:client-new-thread:pending-2");
  f.row.setAttribute("data-app-action-sidebar-thread-host-id", "previous-remote");

  const retainedPage = f.document.createElement("div");
  retainedPage.setAttribute("data-app-shell-active-page", "false");
  retainedPage.css.display = "contents";
  const retainedMarker = f.document.createElement("div");
  retainedMarker.css.display = "none";
  retainedMarker.setAttribute("data-above-composer-conversation-id", "old-thread");
  retainedPage.appendChild(retainedMarker);
  f.document.body.insertBefore(retainedPage, page);

  assert.equal(f.api().selection().threadId, realId);
  assert.equal(f.api().selection().hostId, "local");
  f.update(123, { threadId: realId });
  assert.equal(f.badge().parentElement, f.layout.outer);
  assert.equal(f.badge().hidden, false);
  assert.equal(f.badge().textContent, "123 tok/s");
  f.update(999, { threadId: "old-thread" });
  assert.equal(f.badge().textContent, "123 tok/s");

  f.row.setAttribute("data-app-action-sidebar-thread-id", `local:${realId}`);
  f.composer.setAttribute("data-above-composer-conversation-id", "");
  assert.equal(f.api().selection().threadId, null, "a hidden empty marker still owns the new blank chat");
  assert.equal(f.badge().textContent, "— tok/s");
  f.update(999, { threadId: realId });
  assert.equal(f.api().inspect().rate, null);
  f.api().destroy();
});

test("composer identity stays inside the visible anchor's page and excludes hidden retained trees", () => {
  const f = fixture();
  const page = f.document.createElement("div");
  page.setAttribute("data-app-shell-active-page", "true");
  f.document.body.appendChild(page);
  page.appendChild(f.layout.outer);
  const otherPage = f.document.createElement("div");
  otherPage.setAttribute("data-app-shell-active-page", "true");
  f.document.body.insertBefore(otherPage, page);
  otherPage.appendChild(f.composer);

  const hiddenTree = f.document.createElement("div");
  hiddenTree.css.display = "none";
  page.appendChild(hiddenTree);
  const retainedMarker = f.document.createElement("div");
  retainedMarker.setAttribute("data-above-composer-conversation-id", "retained-thread");
  hiddenTree.appendChild(retainedMarker);
  const blankComposer = f.document.createElement("div");
  blankComposer.setAttribute("data-above-composer-portal", "true");
  blankComposer.css.display = "none";
  page.appendChild(blankComposer);

  assert.equal(f.api().selection().threadId, null);
  f.update(999);
  assert.equal(f.api().inspect().rate, null);
  blankComposer.setAttribute("data-above-composer-conversation-id", "new-thread");
  assert.equal(f.api().selection().threadId, "new-thread");
  f.update(87, { threadId: "new-thread" });
  assert.equal(f.badge().textContent, "87 tok/s");
  f.api().destroy();
});

test("a hidden marker without an app page boundary cannot replace the visible blank composer", () => {
  const f = fixture();
  f.composer.css.display = "none";
  const blankComposer = f.document.createElement("div");
  blankComposer.setAttribute("data-above-composer-portal", "true");
  f.document.body.appendChild(blankComposer);
  assert.equal(f.api().selection().threadId, null);
  f.update(999);
  assert.equal(f.api().inspect().rate, null);
  f.api().destroy();
});

test("a display:contents composer marker remains usable with a collapsed sidebar", () => {
  const f = fixture();
  f.composer.css.display = "contents";
  f.row.css.display = "none";
  f.row.setAttribute("data-app-action-sidebar-thread-host-id", "remote-host");
  assert.equal(f.api().selection().threadId, "thread-a");
  assert.equal(f.api().selection().hostId, "remote-host");
  f.update(55, { hostId: "remote-host" });
  assert.equal(f.badge().textContent, "55 tok/s");
  f.api().destroy();
});

test("selection rejects non-Codex identities and pages with no permissions composer", () => {
  const f = fixture();
  for (const foreign of ["chatgpt:conversation", "cloudremote:task", "remote:task"]) {
    f.composer.setAttribute("data-above-composer-conversation-id", foreign);
    f.row.setAttribute("data-app-action-sidebar-thread-id", foreign);
    assert.equal(f.api().selection().threadId, null);
    assert.equal(f.api().selection().hostId, null);
    assert.equal(f.badge().hidden, true);
    f.update(999, { threadId: foreign });
    assert.equal(f.api().inspect().rate, null);
  }
  f.composer.setAttribute("data-above-composer-conversation-id", "thread-a");
  f.row.setAttribute("data-app-action-sidebar-thread-id", "local:thread-a");
  assert.equal(f.api().selection().threadId, "thread-a");
  f.anchor.remove();
  assert.equal(f.api().selection().threadId, null);
  assert.equal(f.api().selection().hostId, null);
  assert.equal(f.badge(), null);
  f.api().destroy();
});

test("separate renderer windows expose independent selections and measurements", () => {
  const first = fixture();
  const second = fixture();
  second.composer.setAttribute("data-above-composer-conversation-id", "second-thread");
  second.row.setAttribute("data-app-action-sidebar-thread-id", "local:second-thread");
  second.row.setAttribute("data-app-action-sidebar-thread-host-id", "remote-host");
  assert.equal(first.api().selection().threadId, "thread-a");
  assert.equal(second.api().selection().threadId, "second-thread");
  assert.equal(second.api().selection().hostId, "remote-host");
  first.update(123);
  second.update(55, { threadId: "second-thread", hostId: "remote-host" });
  assert.equal(first.badge().textContent, "123 tok/s");
  assert.equal(second.badge().textContent, "55 tok/s");
  first.api().destroy();
  assert.equal(second.badge().textContent, "55 tok/s");
  second.api().destroy();
});

test("installation is idempotent and destroy releases all owned resources", () => {
  const f = fixture();
  const api = f.api();
  const badge = f.badge();
  const before = f.resources();
  f.install();
  assert.equal(f.api(), api);
  assert.equal(f.badge(), badge);
  assert.deepEqual(f.resources(), before);
  f.update(100);
  f.update(200);
  assert.ok(f.resources().frames > 0);
  api.destroy();
  api.destroy();
  assert.equal(f.api(), undefined);
  assert.equal(f.badge(), null);
  assert.equal(f.document.head.children.length, 0);
  assert.deepEqual(f.resources(), {
    frames: 0, timers: 0, mutationObservers: 0, resizeObservers: 0, listeners: 0,
  });
  f.install();
  assert.equal(f.badge().textContent, "— tok/s");
  f.api().destroy();
});

test("upgrading a running v13 destroys its observers and frames before installing v14", () => {
  const f = fixture();
  const oldApi = f.api();
  oldApi.version = 13;
  f.update(100);
  f.update(200);
  assert.ok(f.resources().frames > 0);
  f.install();
  assert.equal(oldApi.inspect().installed, false);
  assert.equal(f.api().version, 14);
  assert.notEqual(f.api(), oldApi);
  assert.equal(f.document.querySelectorAll(badgeSelector).length, 1);
  assert.equal(f.document.head.children.length, 1);
  assert.equal(f.badge().textContent, "— tok/s");
  assert.deepEqual(f.resources(), {
    frames: 0, timers: 1, mutationObservers: 1, resizeObservers: 1, listeners: 7,
  });
  f.update(123);
  assert.equal(f.badge().textContent, "123 tok/s");
  f.api().destroy();
});


test("numeric and unit slots remain separate across digit changes with a settled accessible value", () => {
  const f = fixture();
  const badge = f.badge();
  const value = badge.querySelector("[data-speed-value]");
  const unit = badge.querySelector("[data-speed-unit]");
  assert.equal(badge.children[0].tagName, "BUTTON");
  assert.equal(value.textContent, "—");
  assert.equal(unit.textContent, " tok/s");
  for (const rate of [1, 12, 123, 1234, 10000]) {
    f.update(rate);
    assert.equal(badge.children[0].getAttribute("aria-label"), `${rate === 10000 ? "10k" : rate} tok/s`);
    f.advance(180);
    assert.equal(value.textContent, rate === 10000 ? "10k" : String(rate));
    assert.equal(badge.querySelector("[data-speed-unit]"), unit);
    assert.equal(badge.getBoundingClientRect().width, 800);
  }
  f.update(null);
  assert.equal(value.textContent, "—");
  assert.equal(badge.children[0].getAttribute("aria-label"), `— tok/s`);
  f.api().destroy();
});


test("compact values promote to millions before rounded thousands exceed the numeric slot", () => {
  const f = fixture({ reducedMotion: true });
  for (const [rate, expected] of [[9999, "9999"], [10000, "10k"], [999499, "999k"],
    [999500, "1M"], [1000000, "1M"], [12000000, "12M"]]) {
    f.update(rate);
    assert.equal(f.badge().textContent, `${expected} tok/s`);
    assert.equal(f.badge().querySelector("[data-speed-value]").textContent, expected);
  }
  f.api().destroy();
});

const totals = (extra = {}) => ({hostId:"local",threadId:"thread-a",turns:3,steps:103,
  inputTokens:8918570,cachedInputTokens:8734336,outputTokens:90850,updatedAtMs:100,
  complete:true,source:"agent-speed/1.4.1",...extra});
const clickStat = (f,kind) => f.badge().querySelector(`[data-stat-trigger="${kind}"]`).dispatchEvent({type:"click"});

test("DSH-style session totals show independently of a valid speed and expose exact usage", () => {
  const f=fixture(); f.update(null,{stats:totals()});
  assert.equal(f.api().inspect().summaryText,"3 轮 103 步 · — tok/s9M tok · 缓存命中 98%");
  clickStat(f,"usage");
  const info=f.api().inspect();
  assert.equal(info.openPanel,"usage");
  for(const value of ["9,009,420 tok","184,234 tok","8,734,336 tok","90,850 tok","98%"])
    assert.ok(info.panelText.includes(value),value);
  assert.equal(f.layout.box.contains(f.badge()),false);
  f.api().destroy();
});

test("dialogs are exclusive, close on outside or Escape, and preserve keyboard focus", () => {
  const f=fixture();f.update(30,{stats:totals(),model:"test-model",outputTokens:300,
    durationMs:10000,completedAtMs:1793073600000});
  clickStat(f,"time");assert.equal(f.api().inspect().openPanel,"time");
  assert.ok(f.api().inspect().panelText.includes("10 秒"));
  assert.ok(f.api().inspect().panelText.includes("test-model"));
  clickStat(f,"usage");
  assert.equal(f.document.querySelectorAll("[data-speed-panel]").length,1);
  assert.equal(f.api().inspect().openPanel,"usage");
  let stopped=false;
  f.document.dispatchEvent({type:"keydown",key:"Escape",target:f.document.activeElement,stopPropagation(){stopped=true;}});
  assert.ok(stopped);assert.equal(f.api().inspect().openPanel,null);
  assert.equal(f.document.activeElement,f.badge().querySelector('[data-stat-trigger="usage"]'));
  clickStat(f,"time"); f.document.dispatchEvent({type:"pointerdown",target:f.document.body});
  assert.equal(f.api().inspect().openPanel,null);
  f.api().destroy();
});

test("keyboard focus leaving a nonmodal dialog closes it without taking native focus or Escape", () => {
  const f=fixture();f.update(null,{stats:totals()});clickStat(f,"usage");
  assert.equal(f.document.activeElement,f.document.querySelector("[data-speed-panel]"));
  f.model.focus();
  assert.equal(f.api().inspect().openPanel,null,"Tab into native controls dismisses the nonmodal dialog");
  assert.equal(f.document.activeElement,f.model);
  let stopped=false;
  f.document.dispatchEvent({type:"keydown",key:"Escape",target:f.model,stopPropagation(){stopped=true;}});
  assert.equal(stopped,false,"native Escape remains available after focus leaves the details");
  f.api().destroy();
});

test("an outside or already handled Escape cannot be consumed by an open statistics dialog", () => {
  const f=fixture();f.update(null,{stats:totals()});clickStat(f,"usage");
  const panel=f.document.querySelector("[data-speed-panel]");
  let stopped=false;
  const escape={type:"keydown",key:"Escape",stopPropagation(){stopped=true;}};
  f.document.dispatchEvent({...escape,target:f.model});
  assert.equal(stopped,false);
  assert.equal(f.api().inspect().openPanel,"usage");
  f.document.dispatchEvent({...escape,target:panel,defaultPrevented:true});
  assert.equal(stopped,false);
  assert.equal(f.api().inspect().openPanel,"usage");
  f.document.dispatchEvent({...escape,target:panel});
  assert.equal(stopped,true);
  assert.equal(f.api().inspect().openPanel,null);
  f.api().destroy();
});

test("losing usage or an expired bridge restores dialog focus to an available statistics trigger", () => {
  const f=fixture();f.update(null,{stats:totals()});clickStat(f,"usage");
  const time=f.badge().querySelector('[data-stat-trigger="time"]');
  const usage=f.badge().querySelector('[data-stat-trigger="usage"]');
  f.update(null);
  assert.equal(usage.hidden,true);
  assert.equal(f.api().inspect().openPanel,null);
  assert.equal(f.document.activeElement,time,"a hidden usage trigger cannot receive restored focus");
  f.update(null,{stats:totals()});clickStat(f,"usage");
  f.advance(1800);
  assert.equal(f.api().inspect().stale,true);
  assert.equal(f.api().inspect().openPanel,null);
  assert.equal(f.document.activeElement,time);
  f.api().destroy();
});

test("switching closes details and rejects statistics for a different host or thread", () => {
  const f=fixture();f.update(30,{stats:totals()});clickStat(f,"usage");
  f.composer.setAttribute("data-above-composer-conversation-id","b");f.advance(16);
  assert.equal(f.api().inspect().openPanel,null);assert.equal(f.api().inspect().stats,null);
  f.update(null,{threadId:"b",stats:totals()});assert.equal(f.api().inspect().stats,null);
  f.update(null,{threadId:"b",stats:totals({threadId:"b",hostId:"remote"})});
  assert.equal(f.api().inspect().stats,null);
  assert.equal(f.badge().querySelector('[data-stat-trigger="usage"]').hidden,true);
  f.api().destroy();
});

test("unknown turns and incomplete history are disclosed, and reconnect retains numeric totals", () => {
  const f=fixture();const stats=totals({turns:null,complete:false});
  f.update(null,{stats,status:"cached"});clickStat(f,"time");
  assert.ok(f.api().inspect().summaryText.startsWith("— 轮 103 步"));
  assert.ok(f.api().inspect().panelText.includes("历史记录不完整"));
  f.advance(1000);f.update(null,{stats,status:"cached"});f.advance(1000);
  assert.equal(f.api().inspect().stats.steps,103);
  f.advance(800);assert.equal(f.api().inspect().stats,null);
  assert.equal(f.api().inspect().openPanel,null);
  f.api().destroy();
});

test("cache percentages distinguish near-full from full hits and avoid zero-denominator claims", () => {
  const f=fixture();
  for (const [input,cached,expected] of [[1000,999,"99.9%"],[1000,1000,"100%"],[1000,0,"0%"]]) {
    f.update(null,{stats:totals({inputTokens:input,cachedInputTokens:cached,outputTokens:10})});
    assert.ok(f.api().inspect().summaryText.includes("缓存命中 "+expected));
  }
  f.update(null,{stats:totals({inputTokens:0,cachedInputTokens:0,outputTokens:10})});
  assert.equal(f.api().inspect().summaryText.includes("缓存命中"),false);
  clickStat(f,"usage");assert.ok(f.api().inspect().panelText.includes("未知"));
  f.api().destroy();
});

test("destroy with an open dialog removes owned UI and listeners", () => {
  const f=fixture();f.update(null,{stats:totals()});clickStat(f,"usage");f.api().destroy();
  assert.equal(f.document.querySelector("[data-speed-panel]"),null);
  assert.deepEqual(f.resources(),{frames:0,timers:0,mutationObservers:0,resizeObservers:0,listeners:0});
});


test("slow positive completions never round down to a false zero", () => {
  const f=fixture({reducedMotion:true});
  for(const [rate,text] of [[0.1040317,"0.1 tok/s"],[0.004,"<0.1 tok/s"],[0,"0 tok/s"]]) {
    f.update(rate);assert.equal(f.api().inspect().text,text);
    assert.equal(f.api().inspect().rate,rate);
  }
  f.api().destroy();
});
