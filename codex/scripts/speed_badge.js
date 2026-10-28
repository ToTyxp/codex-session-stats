(() => {
  "use strict";

  const KEY = "__codexLiveSpeedBadge";
  const VERSION = 14;
  if (window[KEY]?.version === VERSION) return window[KEY].inspect();
  if (typeof window[KEY]?.destroy === "function") window[KEY].destroy();

  const ANCHOR = '[data-composer-navigation-target="permissions"]';
  const ROW = "[data-app-action-sidebar-thread-row]";
  const COMPOSER_ID = "[data-above-composer-conversation-id]";
  const COMPOSER_PORTAL = "[data-above-composer-portal]";
  const STYLE_ID = "codex-live-speed-badge-style";
  const OWN = "data-codex-live-speed-badge";
  const TTL = 1500;
  const DURATION = 180;
  const FADE_DURATION = 120;
  const identityAttributes = [
    "data-app-action-sidebar-thread-active", "data-app-shell-active-page",
    "data-app-action-sidebar-thread-id", "data-app-action-sidebar-thread-host-id",
    "data-above-composer-conversation-id", "data-above-composer-portal", "data-above-composer-host-id", "aria-current",
    "data-composer-navigation-target",
  ];
  const relevantSelector = [ANCHOR, ROW, COMPOSER_ID, COMPOSER_PORTAL, "[data-app-action-sidebar-thread-active]"].join(",");
  const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const now = () => performance.now();
  const attr = (node, key) => node ? node.getAttribute(key) : null;
  const pendingThread = value => typeof value === "string" && /^client-new-thread:/.test(value.replace(/^local:/, ""));
  const normalizeThread = value => {
    if (typeof value !== "string") return null;
    const id = value.replace(/^local:/, "").trim();
    return id && !/^(?:chatgpt|cloudremote|remote|client-new-thread):/.test(id) ? id : null;
  };
  const px = value => Number.parseFloat(value) || 0;
  const ease = value => 1 - Math.pow(1 - value, 3);
  let destroyed = false;
  let anchor = null;
  let parent = null;
  let host = null;
  let label = null;
  let value = null;
  let counts = null;
  let timeButton = null;
  let usageButton = null;
  let usageLabel = null;
  let panel = null;
  let openPanel = null;
  let roundedBox = null;
  let threadId = null;
  let hostId = null;
  let latest = null;
  let currentRate = null;
  let animation = null;
  let fade = null;
  let animationFrame = 0;
  let refreshFrame = 0;
  let reducedMotion = motion.matches;
  let stale = false;

  const style = document.createElement("style");
  style.id = STYLE_ID;
  style.textContent = `
    [${OWN}] {
      display:flex; justify-content:center; gap:12px; box-sizing:border-box;
      width:100%; min-width:0; max-width:100%; padding-top:6px;
      font-family:inherit; font-size:12px; font-weight:400; line-height:20px;
      font-variant-numeric:tabular-nums; color:light-dark(#7d818a,#a4a7ae);
    }
    [${OWN}] button {
      display:inline-flex; align-items:center; gap:6px; min-width:0; max-width:100%;
      padding:1px 8px; border:0; border-radius:999px; background:transparent;
      font:inherit; color:inherit; cursor:pointer; white-space:nowrap;
    }
    [${OWN}] button:hover, [${OWN}] button[aria-expanded="true"] {
      background:light-dark(#00000007,#ffffff0d); color:light-dark(#555960,#d2d4d8);
    }
    [${OWN}] button:focus-visible { outline:2px solid #799be5; outline-offset:2px; }
    [${OWN}] svg, [data-speed-panel] svg { flex:none; width:14px; height:14px; }
    [${OWN}] [data-stat-label] { min-width:0; overflow:hidden; text-overflow:ellipsis; }
    [${OWN}] [data-speed-value] { display:inline-block; min-width:3ch; text-align:right; }
    [${OWN}][hidden], [${OWN}] [hidden] { display:none !important; }
    [data-speed-panel] {
      position:fixed; z-index:1100; box-sizing:border-box; border-radius:12px;
      background:light-dark(#fff,#292a2e); color:light-dark(#555960,#d2d4d8);
      width:340px; max-width:calc(100vw - 24px); max-height:calc(100vh - 24px); overflow:auto;
      box-shadow:0 8px 30px #00000024,0 0 0 1px light-dark(#00000012,#ffffff1a);
      padding:16px; font:12px/18px -apple-system,system-ui,sans-serif;
    }
    [data-speed-panel] header { display:flex; justify-content:space-between; align-items:center;
      gap:16px; padding-bottom:8px; margin-bottom:10px; border-bottom:1px solid light-dark(#eee,#444);
      font-weight:500; color:light-dark(#24272c,#eceef0); }
    [data-speed-panel] dl { display:grid; grid-template-columns:minmax(76px,auto) minmax(0,1fr); gap:6px 16px; margin:0; }
    [data-speed-panel] dt, [data-speed-panel] dd { margin:0; min-width:0; }
    [data-speed-panel] dt { color:light-dark(#7d818a,#a4a7ae); }
    [data-speed-panel] [data-panel-title] { display:inline-flex; align-items:center; gap:6px; }
    [data-speed-panel] dd { text-align:right; font-variant-numeric:tabular-nums; overflow-wrap:anywhere; }
    [data-speed-panel] p { margin:12px 0 0; color:light-dark(#7d818a,#a4a7ae); }
  `;
  (document.head || document.documentElement).appendChild(style);

  function visible(node) {
    if (!node || !node.isConnected || node.closest('[data-app-shell-active-page="false"]') ||
        !node.getClientRects().length) return false;
    const css = getComputedStyle(node);
    return css.display !== "none" && css.visibility !== "hidden";
  }

  function inActivePage(selector, activeAnchor = null) {
    const activePage = activeAnchor?.closest("[data-app-shell-active-page]");
    return Array.from(document.querySelectorAll(selector)).find(node => {
      if (node.closest('[data-app-shell-active-page="false"]')) return false;
      if (activeAnchor) {
        if (activePage && node.closest("[data-app-shell-active-page]") !== activePage) return false;
        // Native empty portal markers can be display:none inside the visible
        // composer's page. Only that explicit shared page permits a hidden
        // marker; hidden ancestors and unscoped retained composers stay out.
        const sameActivePage = attr(activePage, "data-app-shell-active-page") === "true";
        for (let parent = sameActivePage ? node.parentElement : node; parent; parent = parent.parentElement) {
          const css = getComputedStyle(parent);
          if (css.display === "none" || css.visibility === "hidden") return false;
        }
      }
      return true;
    }) || null;
  }

  function activeIdentity(activeAnchor) {
    const row = inActivePage(`${ROW}[data-app-action-sidebar-thread-active="true"]`) ||
      inActivePage(`${ROW}[aria-current="page"]`) ||
      inActivePage('[data-app-action-sidebar-thread-active="true"]') ||
      inActivePage(`${ROW}[data-app-action-sidebar-thread-active]:not([data-app-action-sidebar-thread-active="false"])`);
    const rawRowThread = attr(row, "data-app-action-sidebar-thread-id") ||
      attr(row && row.querySelector("[data-app-action-sidebar-thread-id]"), "data-app-action-sidebar-thread-id");
    const rowThread = normalizeThread(rawRowThread);
    // Anonymous portals can precede the actual identity marker in DOM order.
    // An explicitly present ID attribute (including an empty pending ID) wins.
    const composer = inActivePage(COMPOSER_ID, activeAnchor) || inActivePage(COMPOSER_PORTAL, activeAnchor);
    const rawComposerThread = attr(composer, "data-above-composer-conversation-id");
    const composerThread = normalizeThread(rawComposerThread);
    const selectedHost = attr(composer, "data-above-composer-host-id") ||
      ((!composer || (composerThread && rowThread === composerThread)) ?
        attr(row, "data-app-action-sidebar-thread-host-id") : null) || "local";
    if ((composer && rawComposerThread && !composerThread && !pendingThread(rawComposerThread)) ||
        (!composer && rawRowThread && !rowThread && !pendingThread(rawRowThread))) return { threadId: null, hostId: null };
    // The visible composer owns identity. A new thread can receive its real ID
    // while the sidebar still carries a client-new-thread placeholder or old ID.
    // Only a matching sidebar row can supply the composer's missing host.
    // An existing empty composer marker is a new chat, even if the sidebar still
    // marks the previous conversation active. Never reuse that previous ID.
    return { threadId: composer ? composerThread : rowThread, hostId: selectedHost };
  }

  function placement(target) {
    // The presentation node owns the rounded input border. Mount its sibling,
    // never another row inside the native controls or scrollable input surface.
    for (let node = target.parentElement, depth = 0; node && depth < 9; node = node.parentElement, depth++) {
      if (attr(node, "role") !== "presentation" || px(getComputedStyle(node).borderRadius) <= 0) continue;
      const container = node.parentElement;
      if (!container) return null;
      const css = getComputedStyle(container);
      if (!(css.display === "block" || (css.display === "flex" && css.flexDirection === "column")) ||
          !["visible", ""].includes(css.overflow || "")) return null;
      return { container, box: node };
    }
    return null;
  }

  // Formatting and icon artwork adapted from DSH Desktop 2.0.17 StatsPills
  // and ui-primitives (MIT). See UPSTREAM.md and LICENSE-DeepSeek-Harness.
  function formatTokens(n) {
    const scaled = v => v >= 100 ? String(Math.round(v)) : String(Math.round(v * 10) / 10);
    return n < 1000 ? String(n) : n < 1000000 ? scaled(n / 1000) + "K" : scaled(n / 1000000) + "M";
  }

  function cachePercent(stats) {
    if (!stats || !stats.inputTokens) return null;
    if (stats.cachedInputTokens === stats.inputTokens) return "100";
    const percent = stats.cachedInputTokens / stats.inputTokens * 100;
    for (let places = 0; places <= 14; places++) {
      const rounded = percent.toFixed(places);
      if (Number(rounded) < 100) return rounded;
    }
    return "99.99999999999999";
  }

  function element(tag, attrs = {}, text = null) {
    const node = document.createElement(tag);
    for (const [name, value] of Object.entries(attrs)) node.setAttribute(name, value);
    if (text !== null) node.textContent = text;
    return node;
  }

  const artwork = {"time": [{"d": "M3.4041 13.096C2.49514 12.187 1.87614 11.0288 1.62537 9.76798C1.37459 8.50716 1.50331 7.20028 1.99525 6.01261C2.48719 4.82494 3.32025 3.80981 4.3891 3.09557C5.45795 2.38134 6.71458 2.00008 8.0001 2C9.28563 2.00008 10.5423 2.38134 11.6111 3.09557C12.68 3.80981 13.513 4.82494 14.005 6.01261C14.4969 7.20028 14.6256 8.50716 14.3748 9.76798C14.1241 11.0288 13.5051 12.187 12.5961 13.096", "stroke": "currentColor"}, {"d": "M8 8.49994L11.6114 4.88855", "stroke": "currentColor"}, {"d": "M8 9.75C8.69036 9.75 9.25 9.19036 9.25 8.5C9.25 7.80964 8.69036 7.25 8 7.25C7.30964 7.25 6.75 7.80964 6.75 8.5C6.75 9.19036 7.30964 9.75 8 9.75Z", "fill": "currentColor"}], "usage": [{"d": "M13.1967 5.1869C13.7232 4.77378 14.0003 4.30517 14.0001 3.82819C14.0003 3.3512 13.7232 2.88259 13.1967 2.46947C12.6702 2.05635 11.9128 1.71328 11.0006 1.47475C10.0885 1.23621 9.05371 1.11062 8.00039 1.1106C6.94707 1.11057 5.9123 1.23612 5.00009 1.47461C4.08742 1.71301 3.32948 2.05604 2.80249 2.46919C2.2755 2.88235 1.99805 3.35106 1.99805 3.82819C1.99805 4.30531 2.2755 4.77402 2.80249 5.18718C3.32948 5.60033 4.08742 5.94336 5.00009 6.18176C5.9123 6.42025 6.94707 6.5458 8.00039 6.54578C9.05371 6.54575 10.0885 6.42016 11.0006 6.18163C11.9128 5.94309 12.6702 5.60002 13.1967 5.1869Z", "stroke": "currentColor"}, {"d": "M2 3.80371V11.7848", "stroke": "currentColor"}, {"d": "M14 3.80371V11.7848", "stroke": "currentColor"}, {"d": "M2 7.81396C2 8.60524 2.63214 9.36411 3.75736 9.92363C4.88258 10.4832 6.4087 10.7975 8 10.7975C9.5913 10.7975 11.1174 10.4832 12.2426 9.92363C13.3679 9.36411 14 8.60524 14 7.81396", "stroke": "currentColor"}, {"d": "M2 11.7847C2 12.6081 2.63214 13.3977 3.75736 13.98C4.88258 14.5622 6.4087 14.8893 8 14.8893C9.5913 14.8893 11.1174 14.5622 12.2426 13.98C13.3679 13.3977 14 12.6081 14 11.7847", "stroke": "currentColor"}]};
  function icon(kind) {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    for (const [key, val] of Object.entries({viewBox:"0 0 16 16",fill:"none","stroke-width":"1","aria-hidden":"true"})) svg.setAttribute(key, val);
    for (const attrs of artwork[kind]) {
      const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
      for (const [key, val] of Object.entries(attrs)) path.setAttribute(key, val);
      svg.appendChild(path);
    }
    return svg;
  }

  function format(rate) {
    if (rate === null) return "—";
    if (rate > 0 && rate < 0.1) return "<0.1";
    if (rate > 0 && rate < 1) return String(Math.round(rate * 10) / 10);
    const rounded = Math.round(rate);
    if (rounded < 10000) return String(rounded);
    // Promote before rounding would produce five characters (1000k).
    return rounded < 999500 ? `${Math.round(rounded / 1000)}k` : `${Math.round(rounded / 1000000)}M`;
  }

  function sampledRate(time) {
    if (!animation) return currentRate;
    const progress = Math.max(0, Math.min(1, (time - animation.start) / DURATION));
    currentRate = animation.from + (animation.to - animation.from) * ease(progress);
    if (progress === 1) animation = null;
    return currentRate;
  }

  function paint(time) {
    if (!label) return;
    const text = format(sampledRate(time));
    if (value.textContent !== text) value.textContent = text;
    const opacity = fade ? .35 + .65 * Math.min(1, (time - fade.start) / FADE_DURATION) : 1;
    label.style.opacity = String(opacity);
    if (fade && time - fade.start >= FADE_DURATION) fade = null;
  }

  function requestAnimation() {
    if (destroyed || animationFrame || (!animation && !fade)) return;
    animationFrame = requestAnimationFrame(time => {
      animationFrame = 0;
      paint(time);
      requestAnimation();
    });
  }

  function setRate(rate, time = now()) {
    const previous = sampledRate(time);
    const nextTarget = animation ? animation.to : currentRate;
    if (rate === nextTarget) return;
    animation = null;
    if (reducedMotion || rate === null || previous === null) {
      currentRate = rate;
      fade = !reducedMotion && ((previous === null) !== (rate === null)) ? { start: time } : null;
    } else {
      currentRate = previous;
      animation = { from: previous, to: rate, start: time };
    }
    paint(time);
    requestAnimation();
  }

  function updateTooltip() {
    if (!host) return;
    const stats = latest?.stats;
    const countText = stats ? `${stats.turns ?? "—"} 轮 ${stats.steps} 步 · ` : "";
    counts.textContent = countText;
    const rateText = `${format(latest?.rate ?? null)} tok/s`;
    host.title = "最近一次已完成模型调用均速（含推理与首 token 等待）";
    if (latest?.model) host.title += ` · ${latest.model}`;
    if (Number.isFinite(latest?.completedAtMs)) host.title += ` · 完成于 ${new Date(latest.completedAtMs).toLocaleString()}`;
    if (latest?.retained) host.title += " · 上次有效结果";
    timeButton.title = host.title;
    timeButton.setAttribute("aria-label", countText + rateText);
    const hasUsage = !!stats && (stats.inputTokens > 0 || stats.outputTokens > 0);
    usageButton.hidden = !hasUsage;
    if (!hasUsage) {
      usageLabel.textContent = "";
      if (openPanel === "usage") closePanel(true);
    }
    if (hasUsage) {
      const percent = cachePercent(stats);
      usageLabel.textContent = `${formatTokens(stats.inputTokens + stats.outputTokens)} tok` +
        (percent === null ? "" : ` · 缓存命中 ${percent}%`);
      usageButton.setAttribute("aria-label", usageLabel.textContent);
      usageButton.title = stats.complete ? "当前会话累计 Token 用量" : "当前可读取记录的用量，历史不完整";
    }
    if (openPanel) renderPanel();
  }

  function closePanel(restoreFocus = false) {
    const hadFocus = !!panel?.contains(document.activeElement);
    const trigger = openPanel === "time" || usageButton?.hidden ? timeButton : usageButton;
    panel?.remove(); panel = null; openPanel = null;
    timeButton?.setAttribute("aria-expanded", "false");
    usageButton?.setAttribute("aria-expanded", "false");
    if (restoreFocus && hadFocus && trigger?.isConnected && !host?.hidden) trigger.focus();
  }

  function togglePanel(kind) {
    if (openPanel === kind) { closePanel(true); return; }
    closePanel(); openPanel = kind;
    panel = element("div", {"data-speed-panel":"",role:"dialog",tabindex:"-1"});
    document.body.appendChild(panel);
    (kind === "time" ? timeButton : usageButton).setAttribute("aria-expanded", "true");
    renderPanel(); panel.focus();
  }

  function renderPanel() {
    if (!panel) return;
    const stats = latest?.stats;
    const exact = n => Number(n).toLocaleString("en-US") + " tok";
    const rows = [];
    const title = openPanel === "time" ? "会话统计" : "Token 用量";
    let note;
    if (openPanel === "time") {
      if (stats) rows.push(["轮数", stats.turns === null ? "未知" : String(stats.turns)], ["模型调用", `${stats.steps} 步`]);
      if (latest?.rate != null) {
        rows.push(["最近调用模型", latest.model || "未知"], ["调用耗时", `${Math.round(latest.durationMs / 100) / 10} 秒`],
          ["输出（含推理）", exact(latest.outputTokens)], ["最近调用均速", `${format(latest.rate)} tok/s`],
          ["完成时间", new Date(latest.completedAtMs).toLocaleString()]);
      }
      note = "轮＝有完成调用的执行轮次；步＝完成的模型调用，工具不另计步。均速包含推理及首 token 等待。";
      if (!rows.length) note = "等待当前会话的首次完成记录。";
    } else if (stats) {
      const percent = cachePercent(stats);
      rows.push(["缓存命中", percent === null ? "未知" : percent + "%"],
        ["未缓存输入", exact(stats.inputTokens - stats.cachedInputTokens)], ["缓存读取", exact(stats.cachedInputTokens)],
        ["输出", exact(stats.outputTokens)]);
      note = "当前会话自身累计；输入已包含缓存读取，输出已包含推理，不重复相加。";
    }
    if (stats && !stats.complete) note += " 历史记录不完整，当前为可读取部分。";
    const signature = JSON.stringify([title, rows, note]);
    if (panel.getAttribute("data-content") !== signature) {
      panel.setAttribute("data-content", signature);
      panel.setAttribute("aria-label", title);
      panel.textContent = "";
      const heading = element("header");
      const headingLabel = element("span", {"data-panel-title":""});
      headingLabel.appendChild(icon(openPanel));
      headingLabel.appendChild(element("span", {}, title));
      heading.appendChild(headingLabel);
      if (openPanel === "usage" && stats) heading.appendChild(element("span", {}, exact(stats.inputTokens + stats.outputTokens)));
      panel.appendChild(heading);
      const list = element("dl");
      for (const [name, val] of rows) { list.appendChild(element("dt", {}, name)); list.appendChild(element("dd", {}, val)); }
      panel.appendChild(list);
      panel.appendChild(element("p", {}, note));
    }
    positionPanel();
  }

  function positionPanel() {
    if (!panel) return;
    const trigger = openPanel === "time" ? timeButton : usageButton;
    const bounds = trigger.getBoundingClientRect(), size = panel.getBoundingClientRect();
    const width = window.innerWidth || document.documentElement.clientWidth;
    const height = window.innerHeight || document.documentElement.clientHeight;
    panel.style.left = `${Math.max(12, Math.min(bounds.left + bounds.width / 2 - size.width / 2, width - size.width - 12))}px`;
    panel.style.top = `${Math.max(12, Math.min(bounds.top - size.height - 8, height - size.height - 12))}px`;
  }

  function onOutside(event) {
    if (panel && !panel.contains(event.target) && !host?.contains(event.target)) closePanel();
  }
  function onKey(event) {
    if (event.key === "Escape" && !event.defaultPrevented && panel &&
        (panel.contains(event.target) || host?.contains(event.target))) {
      event.stopPropagation(); closePanel(true);
    }
  }

  function clearData() {
    closePanel();
    latest = null;
    stale = false;
    updateTooltip();
    setRate(null);
  }

  function unmount() {
    closePanel();
    resizeObserver?.disconnect();
    if (host) host.remove();
    anchor = parent = host = label = value = roundedBox = counts = timeButton = usageButton = usageLabel = null;
  }

  function refresh() {
    if (destroyed) return;
    const nextAnchor = Array.from(document.querySelectorAll(ANCHOR)).find(visible) || null;
    const identity = nextAnchor ? activeIdentity(nextAnchor) : { threadId: null, hostId: null };
    if (identity.threadId !== threadId || identity.hostId !== hostId) {
      threadId = identity.threadId;
      hostId = identity.hostId;
      clearData();
    }
    const point = nextAnchor && placement(nextAnchor);
    if (!point) {
      unmount();
      return;
    }
    if (anchor !== nextAnchor || parent !== point.container || roundedBox !== point.box || !host?.isConnected) {
      unmount(); anchor = nextAnchor; parent = point.container; roundedBox = point.box;
      host = element("div", {[OWN]:"", "aria-live":"off"});
      host.hidden = true;
      timeButton = element("button", {type:"button", "data-stat-trigger":"time", "aria-haspopup":"dialog", "aria-expanded":"false"});
      timeButton.appendChild(icon("time"));
      const timeText = element("span", {"data-stat-label":""});
      counts = element("span", {"data-stat-counts":""}); timeText.appendChild(counts);
      label = element("span", {"data-speed-label":""});
      value = element("span", {"data-speed-value":""});
      label.appendChild(value); label.appendChild(element("span", {"data-speed-unit":""}, " tok/s"));
      timeText.appendChild(label); timeButton.appendChild(timeText);
      timeButton.addEventListener("click", () => togglePanel("time")); host.appendChild(timeButton);
      usageButton = element("button", {type:"button", "data-stat-trigger":"usage", "aria-haspopup":"dialog", "aria-expanded":"false"});
      usageButton.appendChild(icon("usage"));
      usageLabel = element("span", {"data-stat-label":""}); usageButton.appendChild(usageLabel);
      usageButton.addEventListener("click", () => togglePanel("usage")); host.appendChild(usageButton);
      parent.insertBefore(host, roundedBox.nextSibling);
      resizeObserver?.observe(parent); resizeObserver?.observe(roundedBox);
      updateTooltip(); paint(now());
    }
    const hide = hostId === null || document.visibilityState === "hidden";
    if (host.hidden !== hide) host.hidden = hide;
    if (hide) closePanel(); else positionPanel();
  }

  function scheduleRefresh() {
    if (destroyed || refreshFrame) return;
    refreshFrame = requestAnimationFrame(() => {
      refreshFrame = 0;
      refresh();
    });
  }

  function owned(node) {
    return node === style || node === host || node === panel || (host && host.contains(node)) || (panel && panel.contains(node));
  }

  function relevantNode(node) {
    return node.nodeType === 1 && !owned(node) &&
      (node.matches(relevantSelector) || !!node.querySelector(relevantSelector));
  }

  const observer = new MutationObserver(records => {
    if (records.some(record => {
      if (owned(record.target)) return false;
      if (record.type === "attributes") return true;
      if (parent?.contains(record.target) && [...record.addedNodes, ...record.removedNodes].some(node => !owned(node))) return true;
      return [...record.addedNodes, ...record.removedNodes].some(node =>
        node === anchor || node === parent || relevantNode(node));
    })) scheduleRefresh();
  });
  observer.observe(document.documentElement, {
    subtree: true, childList: true, attributes: true, attributeFilter: identityAttributes,
  });
  const resizeObserver = typeof ResizeObserver === "function" ? new ResizeObserver(scheduleRefresh) : null;

  function onMotionChange(event) {
    reducedMotion = event.matches;
    if (reducedMotion) {
      if (animation) currentRate = animation.to;
      animation = fade = null;
      if (animationFrame) cancelAnimationFrame(animationFrame);
      animationFrame = 0;
      paint(now());
    }
  }

  function onVisibilityChange() {
    if (document.visibilityState === "hidden") clearData();
    scheduleRefresh();
  }

  if (motion.addEventListener) motion.addEventListener("change", onMotionChange);
  else motion.addListener(onMotionChange);
  window.addEventListener("resize", scheduleRefresh);
  document.addEventListener("visibilitychange", onVisibilityChange);
  document.addEventListener("pointerdown", onOutside);
  document.addEventListener("focusin", onOutside);
  document.addEventListener("keydown", onKey);
  window.addEventListener("scroll", positionPanel, true);
  const expiryTimer = setInterval(() => {
    if (latest && now() - latest.receivedAt > TTL) {
      stale = true;
      latest = null;
      updateTooltip();
      closePanel(true);
      setRate(null);
    }
  }, 250);

  const api = {
    version: VERSION,
    selection() {
      if (destroyed) return { threadId: null, hostId: null };
      refresh();
      return { threadId, hostId };
    },
    update(payload) {
      if (destroyed) return;
      refresh();
      if (!payload || !threadId || normalizeThread(payload.threadId) !== threadId ||
          (payload.hostId || "local") !== hostId) return api.inspect();
      const receivedAt = now();
      const disconnected = payload.status === "disconnected" || payload.status === "incompatible";
      const rate = !disconnected && typeof payload.rate === "number" &&
        Number.isFinite(payload.rate) && payload.rate >= 0 ? payload.rate : null;
      const candidate = payload.stats;
      const stats = !disconnected && candidate && normalizeThread(candidate.threadId) === threadId &&
        (candidate.hostId || "local") === hostId ? candidate : null;
      latest = { ...payload, stats, rate, receivedAt, retained: rate !== null && payload.retained === true };
      stale = false;
      updateTooltip();
      setRate(rate, receivedAt);
      return api.inspect();
    },
    inspect() {
      return {
        installed: !destroyed, mounted: !!host?.isConnected, visible: !!host?.isConnected && !host.hidden,
        activeThreadId: threadId, activeHostId: hostId,
        threadId: latest?.threadId || null, hostId: latest?.hostId || null,
        status: latest?.status || null, phase: latest?.phase || null, revision: latest?.revision ?? null,
        retained: latest?.retained || false, turnId: latest?.turnId ?? null,
        responseId: latest?.responseId ?? null, model: latest?.model ?? null,
        outputTokens: latest?.outputTokens ?? null, reasoningTokens: latest?.reasoningTokens ?? null,
        durationMs: latest?.durationMs ?? null, completedAtMs: latest?.completedAtMs ?? null,
        source: latest?.source ?? null, metric: latest?.metric ?? null,
        rate: latest?.rate ?? null, displayedRate: currentRate, text: label?.textContent || null,
        stats: latest?.stats ?? null, openPanel, panelText: panel?.textContent ?? null,
        summaryText: host?.textContent ?? null, external: !!host && host.parentElement === roundedBox?.parentElement,
        stale, reducedMotion, animating: !!animation,
      };
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      observer.disconnect();
      unmount();
      style.remove();
      clearInterval(expiryTimer);
      if (animationFrame) cancelAnimationFrame(animationFrame);
      if (refreshFrame) cancelAnimationFrame(refreshFrame);
      animation = fade = null;
      if (motion.removeEventListener) motion.removeEventListener("change", onMotionChange);
      else motion.removeListener(onMotionChange);
      window.removeEventListener("resize", scheduleRefresh);
      document.removeEventListener("visibilitychange", onVisibilityChange);
      document.removeEventListener("pointerdown", onOutside);
      document.removeEventListener("focusin", onOutside);
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("scroll", positionPanel, true);
      if (window[KEY] === api) delete window[KEY];
    },
  };
  window[KEY] = api;
  refresh();
  return api.inspect();
})()
