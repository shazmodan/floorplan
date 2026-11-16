// Floorplan website. Plain JavaScript, no libraries, no network. Reads window.FLOORPLAN_DATA (the analysis
// model) and nothing else. Labels come from the model: a unit's kind decides whether it is called a module,
// namespace, type or project.
(function () {
  "use strict";

  var D = window.FLOORPLAN_DATA;
  var main = document.getElementById("main");
  var SVGNS = "http://www.w3.org/2000/svg";

  // ---------- Theme ----------
  var themeKey = "floorplan-theme";
  function readTheme() { try { return localStorage.getItem(themeKey); } catch (e) { return null; } }
  function writeTheme(t) { try { localStorage.setItem(themeKey, t); } catch (e) { /* private mode */ } }
  function applyTheme(t) { if (t) document.documentElement.setAttribute("data-theme", t); }
  applyTheme(readTheme());
  document.getElementById("theme").addEventListener("click", function () {
    var current = document.documentElement.getAttribute("data-theme") ||
      (window.matchMedia && matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
    var next = current === "dark" ? "light" : "dark";
    applyTheme(next);
    writeTheme(next);
  });

  // ---------- Helpers ----------
  function el(tag, attrs) {
    var node = document.createElement(tag);
    setAttrs(node, attrs);
    for (var i = 2; i < arguments.length; i++) append(node, arguments[i]);
    return node;
  }
  function svg(tag, attrs) {
    var node = document.createElementNS(SVGNS, tag);
    setAttrs(node, attrs);
    for (var i = 2; i < arguments.length; i++) append(node, arguments[i]);
    return node;
  }
  function setAttrs(node, attrs) {
    if (!attrs) return;
    Object.keys(attrs).forEach(function (k) {
      var v = attrs[k];
      if (v === null || v === undefined || v === false) return;
      if (k === "on") Object.keys(v).forEach(function (e) { node.addEventListener(e, v[e]); });
      else if (k === "text") node.textContent = v;
      else node.setAttribute(k, v === true ? "" : v);
    });
  }
  function append(node, child) {
    if (child === null || child === undefined || child === false) return;
    if (Array.isArray(child)) { child.forEach(function (c) { append(node, c); }); return; }
    node.appendChild(typeof child === "string" || typeof child === "number" ? document.createTextNode(String(child)) : child);
  }
  function by(list, key) { var m = {}; list.forEach(function (x) { m[x[key]] = x; }); return m; }
  function groupBy(list, key) {
    var m = {};
    list.forEach(function (x) { var k = typeof key === "function" ? key(x) : x[key]; (m[k] = m[k] || []).push(x); });
    return m;
  }
  function plural(n, word, many) { return n + " " + (n === 1 ? word : (many || word + "s")); }
  function fileName(path) { return path ? path.split(/[\\/]/).pop() : ""; }
  function loc(l) { return l ? fileName(l.file) + ":" + l.startLine : ""; }
  function enc(s) { return encodeURIComponent(s); }

  var KIND_LABEL = { module: ["Module", "Modules"], namespace: ["Namespace", "Namespaces"], type: ["Type", "Types"], project: ["Project", "Projects"] };
  function kindLabel(kind, many) { var k = KIND_LABEL[kind] || ["Unit", "Units"]; return many ? k[1] : k[0]; }
  // The label for "units" across the whole solution: the dominant kind among full-support units.
  var unitWord = (function () {
    var counts = {};
    D.units.forEach(function (u) { if (u.kind !== "project") counts[u.kind] = (counts[u.kind] || 0) + 1; });
    var best = Object.keys(counts).sort(function (a, b) { return counts[b] - counts[a]; })[0] || "module";
    return { one: kindLabel(best), many: kindLabel(best, true) };
  })();

  var CATEGORY_LABEL = {
    "http-in": "HTTP in", "http-out": "HTTP out", "network": "Network", "database": "Database",
    "filesystem-read": "File read", "filesystem-write": "File write", "process": "Process",
    "native": "Native", "environment": "Environment", "nondeterminism": "Nondeterminism"
  };
  // Presentation defaults come from the model (--show changes them).
  var HIDDEN_BY_DEFAULT = {};
  ((D.presentation && D.presentation.hiddenCategories) || ["environment", "nondeterminism"]).forEach(function (c) { HIDDEN_BY_DEFAULT[c] = true; });
  var HIDDEN_CONFIDENCE = {};
  ((D.presentation && D.presentation.hiddenConfidence) || ["low"]).forEach(function (c) { HIDDEN_CONFIDENCE[c] = true; });
  function visibleExit(x) { return !HIDDEN_BY_DEFAULT[x.category] && !HIDDEN_CONFIDENCE[x.confidence]; }
  var LANGUAGE_LABEL = { fsharp: "F#", csharp: "C#", other: "Other" };

  // ---------- Indexes ----------
  var projects = by(D.projects, "id");
  var units = by(D.units, "id");
  var depsFrom = groupBy(D.dependencies, "fromUnit");
  var depsTo = groupBy(D.dependencies, "toUnit");
  var entryByUnit = groupBy(D.entryPoints, "handlerUnit");
  var exitByUnit = groupBy(D.exitPoints, "unit");
  var exitById = by(D.exitPoints, "id");
  var entryById = by(D.entryPoints, "id");
  var traceByEntry = by(D.traces, "entryPointId");
  var unitsByProject = groupBy(D.units, "projectId");

  function unitName(id) { var u = units[id]; return u ? u.name : id; }
  function unitLink(id) {
    var u = units[id];
    if (!u) return el("span", { class: "mono" }, id);
    return el("a", { href: "#/unit/" + enc(id), title: u.name }, u.name);
  }
  function projectName(id) { var p = projects[id]; return p ? p.name : id; }
  // A member as people wrote it. The compiler turns a lambda into a class nested in its module, named after the
  // enclosing binding and the line ("map@24", "Pipe #1 input at line 24@24"), with the body in Invoke (or
  // MoveNext for a task or async body): shown as "Admin.map › lambda at line 24".
  function readableMember(m, short) {
    if (!m) return "";
    var i = m.lastIndexOf("::");
    var type = i >= 0 ? m.slice(0, i) : m, method = i >= 0 ? m.slice(i + 2) : "";
    var owner = function (t) { return short ? t.split(".").pop().replace(/\+/g, ".") : t; };
    var slash = type.indexOf("/"), last = type.slice(type.lastIndexOf("/") + 1), at = last.match(/@(\d+)(?:-\d+)?$/);
    if (slash >= 0 && at) {
      var binding = last.match(/^([A-Za-z_][\w']*)@\d+/);
      var what = method === "MoveNext" ? "async body" : "lambda";
      return owner(type.slice(0, slash)) + (binding ? "." + binding[1] : "") + " › " + what + " at line " + at[1];
    }
    return owner(type) + (i >= 0 ? (short ? "." : "::") + method : "");
  }
  function shortMember(m) { return readableMember(m, true); }

  // ---------- Navigation ----------
  var VIEWS = [
    { id: "overview", label: "Overview" },
    { id: "projects", label: "Projects" },
    { id: "units", label: unitWord.many },
    { id: "entry-points", label: "Entry points", count: function () { return D.entryPoints.length; } },
    { id: "exit-points", label: "Exit points", count: function () { return D.exitPoints.filter(visibleExit).length; } },
    { id: "diagnostics", label: "Diagnostics", count: function () { return D.diagnostics.filter(function (d) { return d.severity !== "info"; }).length; } }
  ];

  function renderNav(active) {
    var nav = document.getElementById("nav");
    nav.textContent = "";
    VIEWS.forEach(function (v) {
      var n = v.count ? v.count() : null;
      nav.appendChild(el("a", { href: "#/" + v.id, "aria-current": v.id === active ? "page" : null },
        v.label, n !== null ? el("span", { class: "count" }, String(n)) : null));
    });
  }

  function parseRoute() {
    var h = location.hash.replace(/^#\/?/, "");
    var q = {};
    var qi = h.indexOf("?");
    if (qi >= 0) {
      h.slice(qi + 1).split("&").forEach(function (kv) { var p = kv.split("="); if (p[0]) q[decodeURIComponent(p[0])] = decodeURIComponent(p[1] || ""); });
      h = h.slice(0, qi);
    }
    var parts = h.split("/");
    return { view: parts[0] || "overview", arg: parts.length > 1 ? decodeURIComponent(parts.slice(1).join("/")) : null, query: q };
  }

  function route() {
    var r = parseRoute();
    var view = {
      "overview": overview, "projects": projectGraph, "units": unitGraph, "unit": unitDetail,
      "entry-points": entryPoints, "exit-points": exitPoints, "evidence": evidence, "diagnostics": diagnostics
    }[r.view] || overview;
    var navId = r.view === "unit" ? "units" : (r.view === "evidence" ? null : r.view);
    renderNav(navId);
    main.textContent = "";
    view(r);
    main.focus({ preventScroll: true });
  }
  window.addEventListener("hashchange", route);

  // ---------- Overview ----------
  function overview() {
    var page = el("div", { class: "page" });
    var apps = D.projects.filter(function (p) { return p.isApplication; });
    var fallback = D.projects.filter(function (p) { return p.catalogFallback; });
    var tfms = Array.from(new Set(D.projects.map(function (p) { return p.targetFramework; }))).sort();
    var catalogs = Array.from(new Set(D.projects.map(function (p) { return p.catalogUsed; }).filter(Boolean))).sort();

    append(page, [
      el("h1", null, D.solution.name),
      el("p", { class: "sub" }, el("span", { class: "mono" }, D.solution.path), " · generated ", D.generatedAt, " · Floorplan ", D.toolVersion)
    ]);

    if (fallback.length) {
      append(page, el("div", { class: "notice warning", role: "alert" },
        el("strong", null, "Newer .NET than the catalog. "),
        fallback.map(function (p) { return p.name + " (" + p.targetFramework + ")"; }).join(", "),
        " target a .NET version with no boundary catalog. The newest catalog (" + (fallback[0].catalogUsed || "?") +
        ") was used, so results may miss primitives added since."));
    }
    var errors = D.diagnostics.filter(function (d) { return d.severity === "error"; });
    if (errors.length) {
      append(page, el("div", { class: "notice error" }, plural(errors.length, "error"), " during analysis. ",
        el("a", { href: "#/diagnostics" }, "See diagnostics.")));
    }

    function card(n, label, href) { return el(href ? "a" : "div", { class: "card", href: href }, el("div", { class: "n" }, String(n)), el("div", { class: "l" }, label)); }
    var visibleExits = D.exitPoints.filter(visibleExit);
    append(page, el("div", { class: "cards" },
      card(D.projects.length, "Projects", "#/projects"),
      card(apps.length, "Applications", "#/projects"),
      card(D.units.filter(function (u) { return u.kind !== "project"; }).length, unitWord.many, "#/units"),
      card(D.dependencies.length, "Dependencies", "#/units"),
      card(D.entryPoints.length, "Entry points", "#/entry-points"),
      card(visibleExits.length, "Exit points", "#/exit-points")));

    append(page, [
      el("p", { class: "muted" }, "Target frameworks: ", tfms.join(", ") || "none",
        catalogs.length ? " · Catalogs: " + catalogs.join(", ") : ""),
      el("h2", null, "Projects"),
      el("div", { class: "table-wrap" }, el("table", null,
        el("thead", null, el("tr", null, ["Project", "Language", "Target", "Kind", unitWord.many, "Entry", "Exit"].map(function (h) { return el("th", { scope: "col" }, h); }))),
        el("tbody", null, D.projects.map(function (p) {
          var us = unitsByProject[p.id] || [];
          var ids = {}; us.forEach(function (u) { ids[u.id] = true; });
          return el("tr", null,
            el("td", null, el("a", { href: "#/units?project=" + enc(p.id) }, p.name)),
            el("td", null, LANGUAGE_LABEL[p.language] || p.language, p.supportLevel === "limited" ? [" ", el("span", { class: "tag limited", title: "Shown as one node; effects attributed to the whole project" }, "limited support")] : null),
            el("td", null, p.targetFramework),
            el("td", null, p.isApplication ? "Application" : "Library"),
            el("td", { class: "num" }, p.supportLevel === "limited" ? "—" : String(us.filter(function (u) { return u.kind !== "project"; }).length)),
            el("td", { class: "num" }, String(D.entryPoints.filter(function (e) { return ids[e.handlerUnit]; }).length)),
            el("td", { class: "num" }, String(visibleExits.filter(function (x) { return ids[x.unit]; }).length)));
        }))))
    ]);
    main.appendChild(page);
  }

  // ---------- SVG graph with pan and zoom ----------
  function graphCanvas(width, height) {
    var pad = 40;
    var root = svg("svg", { role: "img", "aria-label": "Dependency graph" });
    var defs = svg("defs", null,
      svg("marker", { id: "arrow", viewBox: "0 0 10 10", refX: "10", refY: "5", markerWidth: "8", markerHeight: "8", markerUnits: "userSpaceOnUse", orient: "auto-start-reverse" }, svg("path", { d: "M0,0 L10,5 L0,10 z" })),
      svg("marker", { id: "arrow-cross", viewBox: "0 0 10 10", refX: "10", refY: "5", markerWidth: "8", markerHeight: "8", markerUnits: "userSpaceOnUse", orient: "auto-start-reverse" }, svg("path", { d: "M0,0 L10,5 L0,10 z" })));
    var world = svg("g");
    root.appendChild(defs);
    root.appendChild(world);

    var view = { x: -pad, y: -pad, w: Math.max(width, 200) + 2 * pad, h: Math.max(height, 120) + 2 * pad };
    var full = { x: view.x, y: view.y, w: view.w, h: view.h };
    function apply() { root.setAttribute("viewBox", view.x + " " + view.y + " " + view.w + " " + view.h); }
    apply();

    function zoom(factor, cx, cy) {
      var nw = Math.min(Math.max(view.w * factor, 80), full.w * 8);
      var f = nw / view.w;
      view.x = cx - (cx - view.x) * f;
      view.y = cy - (cy - view.y) * f;
      view.w *= f; view.h *= f;
      apply();
    }
    function toWorld(evt) {
      var r = root.getBoundingClientRect();
      var scale = Math.max(view.w / r.width, view.h / r.height);
      var ox = (r.width * scale - view.w) / 2, oy = (r.height * scale - view.h) / 2;
      return { x: view.x - ox + (evt.clientX - r.left) * scale, y: view.y - oy + (evt.clientY - r.top) * scale, scale: scale };
    }
    root.addEventListener("wheel", function (e) {
      e.preventDefault();
      var p = toWorld(e);
      zoom(e.deltaY > 0 ? 1.15 : 1 / 1.15, p.x, p.y);
    }, { passive: false });

    var drag = null;
    root.addEventListener("pointerdown", function (e) {
      if (e.target.closest && e.target.closest(".g-node, .toggle")) return;
      drag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, scale: toWorld(e).scale, moved: false };
      root.setPointerCapture(e.pointerId);
      root.classList.add("panning");
    });
    root.addEventListener("pointermove", function (e) {
      if (!drag) return;
      var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
      if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
      view.x = drag.vx - dx * drag.scale;
      view.y = drag.vy - dy * drag.scale;
      apply();
    });
    function endDrag() { if (drag) { root.classList.remove("panning"); root.dispatchEvent(new CustomEvent("panend", { detail: { moved: drag.moved } })); } drag = null; }
    root.addEventListener("pointerup", endDrag);
    root.addEventListener("pointercancel", endDrag);

    return {
      svg: root, world: world,
      zoomIn: function () { zoom(1 / 1.3, view.x + view.w / 2, view.y + view.h / 2); },
      zoomOut: function () { zoom(1.3, view.x + view.w / 2, view.y + view.h / 2); },
      fit: function () { view.x = full.x; view.y = full.y; view.w = full.w; view.h = full.h; apply(); },
      focus: function (x, y, w, h, minW, minH) {
        var m = 60, vw = Math.max(w + 2 * m, minW || 300), vh = Math.max(h + 2 * m, minH || 200);
        view.x = x + w / 2 - vw / 2; view.y = y + h / 2 - vh / 2; view.w = vw; view.h = vh; apply();
      }
    };
  }

  function zoomButtons(canvas) {
    return [
      el("button", { type: "button", "aria-label": "Zoom in", on: { click: canvas.zoomIn } }, "+"),
      el("button", { type: "button", "aria-label": "Zoom out", on: { click: canvas.zoomOut } }, "−"),
      el("button", { type: "button", on: { click: canvas.fit } }, "Fit")
    ];
  }

  function nodeText(label, maxWidth) {
    var max = Math.max(4, Math.floor((maxWidth - 16) / 7.2));
    return label.length > max ? label.slice(0, max - 1) + "…" : label;
  }

  // ---------- Project graph ----------
  function projectGraph() {
    var g = D.layout.projectGraph;
    var page = el("div", { class: "graph-page" });
    var host = el("div", { class: "graph" });
    var canvas = graphCanvas(g.width, g.height);
    var nodes = by(g.nodes, "id");
    g.nodes.forEach(function (n) { n.members.forEach(function (m) { nodes[m] = n; }); });

    g.edges.forEach(function (e) {
      var p = e.points;
      canvas.world.appendChild(svg("line", { class: "g-edge", x1: p[0].x, y1: p[0].y, x2: p[p.length - 1].x, y2: p[p.length - 1].y }));
    });
    g.nodes.forEach(function (n) {
      var ids = n.members.length ? n.members : [n.id];
      var label = ids.map(projectName).join(" ⇄ ");
      var p = projects[ids[0]] || {};
      var classes = "g-node" + (n.members.length ? " group" : "");
      var go = function () { location.hash = "#/units?project=" + enc(ids[0]); };
      var node = svg("g", { class: classes, tabindex: "0", role: "link", "aria-label": label + (p.isApplication ? ", application" : ""),
        on: { click: go, keydown: function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); go(); } } } },
        svg("rect", { x: n.x, y: n.y, width: n.width, height: n.height, rx: 8 }),
        svg("text", { x: n.x + n.width / 2, y: n.y + n.height / 2 + 4, "text-anchor": "middle", text: nodeText(label, n.width) }),
        svg("title", { text: label + " — " + (p.targetFramework || "") + (p.isApplication ? ", application" : "") + (p.supportLevel === "limited" ? ", limited support" : "") }));
      canvas.world.appendChild(node);
    });

    append(page, [
      el("div", { class: "toolbar" }, el("strong", null, "Projects"), el("span", { class: "muted" }, "Arrows point from a project to the projects it references. Select a project to open its " + unitWord.many.toLowerCase() + "."), el("span", { class: "spacer" }), zoomButtons(canvas)),
      host
    ]);
    host.appendChild(canvas.svg);
    main.appendChild(page);
  }

  // ---------- Unit graph ----------
  var collapsedState = null;

  function curve(points) {
    var d = "M" + points[0].x + "," + points[0].y;
    for (var i = 1; i < points.length; i++) {
      var a = points[i - 1], b = points[i], dy = (b.y - a.y) / 2;
      d += Math.abs(b.x - a.x) < 0.5 ? " L" + b.x + "," + b.y : " C" + a.x + "," + (a.y + dy) + " " + b.x + "," + (b.y - dy) + " " + b.x + "," + b.y;
    }
    return d;
  }

  function unitGraph(r) {
    var g = D.layout.unitGraph;
    var boxes = D.layout.projectBoxes;
    if (!collapsedState) {
      collapsedState = {};
      // Large solutions open with projects collapsed (plan section 7.2).
      if (D.units.length > 150) boxes.forEach(function (b) { collapsedState[b.id] = true; });
    }
    var focusProject = r.query.project;
    if (focusProject && projects[focusProject]) collapsedState[focusProject] = false;
    var selected = r.query.unit || null;

    var page = el("div", { class: "graph-page" });
    var host = el("div", { class: "graph" });
    var canvas = graphCanvas(g.width, g.height);
    var side = el("div", { class: "side", hidden: true });

    var nodeOf = {};
    g.nodes.forEach(function (n) { nodeOf[n.id] = n; n.members.forEach(function (m) { nodeOf[m] = n; }); });
    var boxOf = by(boxes, "id");
    var detachedBoxes = D.layout.detachedBoxes || [];
    var detachedOf = by(detachedBoxes, "id");
    // Units drawn in a project's box of unconnected units.
    var detachedCount = {};
    g.nodes.forEach(function (n) {
      var u = units[n.id], b = u && detachedOf[u.projectId];
      if (b && n.y >= b.y && n.y <= b.y + b.height) detachedCount[u.projectId] = (detachedCount[u.projectId] || 0) + 1;
    });
    var edgesLayer = svg("g");
    var boxesLayer = svg("g");
    var selectionLayer = svg("g");
    var nodesLayer = svg("g");
    canvas.world.appendChild(boxesLayer);
    canvas.world.appendChild(edgesLayer);
    canvas.world.appendChild(selectionLayer);
    canvas.world.appendChild(nodesLayer);

    var nodeEls = {}, boxEls = {}, edgeEls = [];
    var innerEdge = {};
    g.edges.forEach(function (e) { innerEdge[e.from + ">" + e.to] = e; });
    var crossByPair = {};
    D.dependencies.forEach(function (d) {
      var a = units[d.fromUnit], b = units[d.toUnit];
      if (!d.crossProject || !a || !b) return;
      (crossByPair[a.projectId + ">" + b.projectId] = crossByPair[a.projectId + ">" + b.projectId] || []).push(d);
    });

    function visibleEnd(unitId) {
      var u = units[unitId];
      if (!u) return null;
      if (collapsedState[u.projectId]) { var b = boxOf[u.projectId]; return b ? { key: "box:" + b.id, x: b.x, y: b.y, width: b.width, height: 30 } : null; }
      var n = nodeOf[unitId];
      return n ? { key: n.id, x: n.x, y: n.y, width: n.width, height: n.height } : null;
    }

    function drawBoxes() {
      boxesLayer.textContent = "";
      if (detachedBoxes.length) {
        var top = Math.min.apply(null, detachedBoxes.map(function (b) { return b.y; }));
        var left = Math.min.apply(null, detachedBoxes.map(function (b) { return b.x; }));
        boxesLayer.appendChild(svg("text", { class: "g-section", x: left, y: top - 14,
          text: "Not connected: " + unitWord.many.toLowerCase() + " that use no other " + unitWord.one.toLowerCase() + " and are used by none (some are reached only at run time)" }));
      }
      boxes.map(function (b) { return [b, false]; }).concat(detachedBoxes.map(function (b) { return [b, true]; })).forEach(function (pair) {
        var b = pair[0], detached = pair[1];
        var p = projects[b.id] || {};
        var collapsed = !!collapsedState[b.id];
        var h = collapsed ? 30 : b.height;
        var count = detached ? detachedCount[b.id] : (unitsByProject[b.id] || []).length - (detachedCount[b.id] || 0);
        var toggle = function () { collapsedState[b.id] = !collapsedState[b.id]; redraw(); };
        var grp = svg("g", { class: "g-box" + (detached ? " detached" : "") + (p.supportLevel === "limited" ? " limited" : "") + (p.isApplication ? " app" : "") },
          svg("rect", { class: "frame", x: b.x, y: b.y, width: b.width, height: h, rx: 10 }),
          svg("rect", { class: "header", x: b.x + 1, y: b.y + 1, width: b.width - 2, height: 27, rx: 9 }),
          svg("text", { class: "name", x: b.x + 12, y: b.y + 19, text: nodeText(p.name || b.id, b.width - 40) }),
          svg("g", { class: "toggle", tabindex: "0", role: "button", "aria-expanded": String(!collapsed), "aria-label": (collapsed ? "Expand " : "Collapse ") + (p.name || b.id),
            on: { click: toggle, keydown: function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggle(); } } } },
            svg("rect", { x: b.x + b.width - 30, y: b.y + 4, width: 22, height: 20, rx: 4, fill: "transparent" }),
            svg("text", { x: b.x + b.width - 19, y: b.y + 19, "text-anchor": "middle", text: collapsed ? "+" : "−" })),
          svg("title", { text: (p.name || b.id) + " — " + (p.targetFramework || "") + ", " + plural(count, kindLabel("module").toLowerCase()) + (p.supportLevel === "limited" ? ", limited support" : "") }));
        boxEls[(detached ? "detached:" : "") + b.id] = grp;
        boxesLayer.appendChild(grp);
      });
    }

    function drawNodes() {
      nodesLayer.textContent = "";
      nodeEls = {};
      g.nodes.forEach(function (n) {
        var ids = n.members.length ? n.members : [n.id];
        var u = units[ids[0]];
        if (!u || collapsedState[u.projectId]) return;
        var label = ids.map(function (i) { return (units[i] || {}).displayName || i; }).join(" ⇄ ");
        var entries = ids.reduce(function (a, i) { return a + (entryByUnit[i] || []).length; }, 0);
        var exits = ids.reduce(function (a, i) { return a + (exitByUnit[i] || []).filter(visibleExit).length; }, 0);
        var badge = (entries ? "→" + entries : "") + (entries && exits ? " " : "") + (exits ? exits + "→" : "");
        var select = function () { selectUnit(ids[0]); };
        var node = svg("g", { class: "g-node" + (n.members.length ? " group" : ""), tabindex: "0", role: "button",
          "aria-label": kindLabel(u.kind) + " " + ids.map(unitName).join(", "),
          on: {
            click: select,
            dblclick: function () { location.hash = "#/unit/" + enc(ids[0]); },
            keydown: function (e) {
              if (e.key === "Enter") { e.preventDefault(); if (selected === ids[0]) location.hash = "#/unit/" + enc(ids[0]); else select(); }
              else if (e.key === " ") { e.preventDefault(); select(); }
              else if (e.key === "Escape") { selectUnit(null); }
            }
          } },
          svg("rect", { x: n.x, y: n.y, width: n.width, height: n.height, rx: 6 }),
          svg("text", { x: n.x + n.width / 2, y: n.y + n.height / 2 + 4, "text-anchor": "middle", text: nodeText(label, n.width) }),
          badge ? svg("text", { class: "badge", x: n.x + n.width - 4, y: n.y - 3, "text-anchor": "end", text: badge }) : null,
          svg("title", { text: ids.map(unitName).join("\n") + (entries ? "\n" + plural(entries, "entry point") : "") + (exits ? "\n" + plural(exits, "exit point") : "") }));
        ids.forEach(function (i) { nodeEls[i] = node; });
        nodesLayer.appendChild(node);
      });
    }

    function drawEdges() {
      edgesLayer.textContent = "";
      edgeEls = [];
      D.dependencies.forEach(function (d) {
        var u = units[d.fromUnit];
        if (d.crossProject || !u || collapsedState[u.projectId]) return;
        var e = innerEdge[d.fromUnit + ">" + d.toUnit];
        if (!e) return;
        var path = svg("path", { class: "g-edge", d: curve(e.points) },
          svg("title", { text: unitName(d.fromUnit) + " → " + unitName(d.toUnit) + " (" + plural(d.weight, "use") + ")" }));
        path.__deps = [d];
        edgesLayer.appendChild(path);
        edgeEls.push(path);
      });
      (D.layout.projectEdges || []).forEach(function (e) {
        var deps = crossByPair[e.from + ">" + e.to] || [];
        if (!deps.length) return;
        var points = e.points.map(function (p) { return { x: p.x, y: p.y }; });
        var source = boxOf[e.from];
        if (source && collapsedState[e.from]) points[0].y = source.y + 30;
        var path = svg("path", { class: "g-edge between", d: curve(points), style: "stroke-width:" + Math.min(5, 1.5 + Math.log(deps.length) / Math.LN2 * 0.6) },
          svg("title", { text: projectName(e.from) + " → " + projectName(e.to) + ": " + plural(deps.length, unitWord.one.toLowerCase() + " dependency", unitWord.one.toLowerCase() + " dependencies") + ". Select a " + unitWord.one.toLowerCase() + " to see its own." }));
        path.__deps = deps;
        path.__between = true;
        edgesLayer.appendChild(path);
        edgeEls.push(path);
      });
    }

    // The selected unit's dependencies on other projects, drawn from it to the other end.
    function drawSelection() {
      selectionLayer.textContent = "";
      if (!selected) return;
      var mine = (depsFrom[selected] || []).concat(depsTo[selected] || []).filter(function (d) { return d.crossProject; });
      mine.forEach(function (d) {
        var a = visibleEnd(d.fromUnit), b = visibleEnd(d.toUnit);
        if (!a || !b || a.key === b.key) return;
        var down = b.y > a.y;
        var start = { x: a.x + a.width / 2, y: down ? a.y + a.height : a.y };
        var end = { x: b.x + b.width / 2, y: down ? b.y : b.y + b.height };
        selectionLayer.appendChild(svg("path", { class: "g-edge cross " + (d.fromUnit === selected ? "dependency" : "dependent"), d: curve([start, end]) },
          svg("title", { text: unitName(d.fromUnit) + " → " + unitName(d.toUnit) + " (" + plural(d.weight, "use") + ")" })));
      });
    }

    function highlight() {
      var all = [].concat(Object.keys(nodeEls).map(function (k) { return nodeEls[k]; }), edgeEls, Object.keys(boxEls).map(function (k) { return boxEls[k]; }));
      all.forEach(function (x) { x.classList.remove("dim", "dependency", "dependent", "selected"); });
      drawSelection();
      if (!selected) { side.hidden = true; return; }
      var out = {}, inc = {};
      (depsFrom[selected] || []).forEach(function (d) { out[d.toUnit] = true; });
      (depsTo[selected] || []).forEach(function (d) { inc[d.fromUnit] = true; });
      Object.keys(nodeEls).forEach(function (k) {
        var n = nodeEls[k];
        if (k === selected) n.classList.add("selected");
        else if (out[k]) n.classList.add("dependency");
        else if (inc[k]) n.classList.add("dependent");
      });
      Object.keys(nodeEls).forEach(function (k) {
        var n = nodeEls[k];
        if (!n.classList.contains("selected") && !n.classList.contains("dependency") && !n.classList.contains("dependent")) n.classList.add("dim");
      });
      edgeEls.forEach(function (e) {
        var mine = e.__deps.filter(function (d) { return d.fromUnit === selected || d.toUnit === selected; });
        if (!mine.length) e.classList.add("dim");
        else if (!e.__between) e.classList.add(mine[0].fromUnit === selected ? "dependency" : "dependent");
      });
      renderSide();
    }

    function renderSide() {
      var u = units[selected];
      side.textContent = "";
      if (!u) { side.hidden = true; return; }
      side.hidden = false;
      var out = (depsFrom[selected] || []).slice().sort(function (a, b) { return b.weight - a.weight; });
      var inc = (depsTo[selected] || []).slice().sort(function (a, b) { return b.weight - a.weight; });
      append(side, [
        el("h3", null, u.name),
        el("div", { class: "muted" }, kindLabel(u.kind), " in ", projectName(u.projectId), u.location ? " · " + loc(u.location) : ""),
        el("p", null, el("a", { href: "#/unit/" + enc(u.id) }, "Open details")),
        el("h4", null, "Depends on (" + out.length + ")"),
        out.length ? el("ul", { class: "plain" }, out.slice(0, 12).map(function (d) { return el("li", null, unitLink(d.toUnit), el("span", { class: "muted" }, " ×" + d.weight)); })) : el("div", { class: "muted" }, "Nothing"),
        el("h4", null, "Used by (" + inc.length + ")"),
        inc.length ? el("ul", { class: "plain" }, inc.slice(0, 12).map(function (d) { return el("li", null, unitLink(d.fromUnit), el("span", { class: "muted" }, " ×" + d.weight)); })) : el("div", { class: "muted" }, "Nothing")
      ]);
    }

    function selectUnit(id) {
      selected = id;
      highlight();
    }

    function redraw() { drawBoxes(); drawEdges(); drawNodes(); highlight(); }

    // Frames a unit with the units it uses and is used by, so that a selection shows its neighbourhood.
    function focusUnit(id) {
      var ends = [id].concat((depsFrom[id] || []).map(function (d) { return d.toUnit; }), (depsTo[id] || []).map(function (d) { return d.fromUnit; }));
      var boxes = ends.map(visibleEnd).filter(Boolean);
      if (!boxes.length) return;
      var x1 = Math.min.apply(null, boxes.map(function (b) { return b.x; })), y1 = Math.min.apply(null, boxes.map(function (b) { return b.y; }));
      var x2 = Math.max.apply(null, boxes.map(function (b) { return b.x + b.width; })), y2 = Math.max.apply(null, boxes.map(function (b) { return b.y + b.height; }));
      canvas.focus(x1, y1, x2 - x1, y2 - y1, 900, 560);
    }

    canvas.svg.addEventListener("panend", function (e) { if (!e.detail.moved) selectUnit(null); });

    var search = el("input", { type: "search", placeholder: "Find " + unitWord.one.toLowerCase() + "…", "aria-label": "Find " + unitWord.one.toLowerCase(),
      on: { keydown: function (e) {
        if (e.key !== "Enter") return;
        var q = search.value.toLowerCase();
        var hit = D.units.filter(function (u) { return u.name.toLowerCase().indexOf(q) >= 0; })[0];
        if (!hit) return;
        collapsedState[hit.projectId] = false;
        redraw();
        selectUnit(hit.id);
        focusUnit(hit.id);
        if (nodeEls[hit.id]) nodeEls[hit.id].focus();
      } } });

    append(page, [
      el("div", { class: "toolbar" },
        el("strong", null, unitWord.many), search,
        el("button", { type: "button", on: { click: function () { boxes.forEach(function (b) { collapsedState[b.id] = true; }); redraw(); } } }, "Collapse all"),
        el("button", { type: "button", on: { click: function () { boxes.forEach(function (b) { collapsedState[b.id] = false; }); redraw(); } } }, "Expand all"),
        el("span", { class: "spacer" }), zoomButtons(canvas)),
      host
    ]);
    host.appendChild(canvas.svg);
    host.appendChild(side);
    host.appendChild(el("div", { class: "legend", "aria-hidden": "true" },
      el("span", null, el("i"), "within project"), el("span", null, el("i", { class: "between" }), "between projects"),
      el("span", null, el("i", { class: "dep" }), "depends on"), el("span", null, el("i", { class: "dnt" }), "used by")));
    main.appendChild(page);
    redraw();
    if (focusProject && (boxOf[focusProject] || detachedOf[focusProject])) { var b = boxOf[focusProject] || detachedOf[focusProject]; canvas.focus(b.x, b.y, b.width, b.height); }
    if (selected && nodeOf[selected]) focusUnit(selected);
  }

  // ---------- Unit detail ----------
  function unitDetail(r) {
    var u = units[r.arg];
    var page = el("div", { class: "page" });
    if (!u) { append(page, el("p", { class: "empty" }, "Unknown " + unitWord.one.toLowerCase() + ": " + r.arg)); main.appendChild(page); return; }
    var out = (depsFrom[u.id] || []).slice().sort(function (a, b) { return b.weight - a.weight; });
    var inc = (depsTo[u.id] || []).slice().sort(function (a, b) { return b.weight - a.weight; });
    var children = D.units.filter(function (x) { return x.parent === u.id; });
    var p = projects[u.projectId] || {};

    function depTable(list, end) {
      if (!list.length) return el("p", { class: "muted" }, "None.");
      return el("div", { class: "table-wrap" }, el("table", null,
        el("thead", null, el("tr", null, el("th", null, unitWord.one), el("th", null, "Project"), el("th", null, "Uses"), el("th", null, "For example"))),
        el("tbody", null, list.map(function (d) {
          var other = units[d[end]] || {};
          return el("tr", null, el("td", null, unitLink(d[end])), el("td", null, projectName(other.projectId), d.crossProject ? " " : null, d.crossProject ? el("span", { class: "tag" }, "cross-project") : null),
            el("td", { class: "num" }, String(d.weight)), el("td", { class: "mono" }, d.sampleLocations.map(loc).join(", ")));
        }))));
    }

    append(page, [
      el("p", { class: "muted" }, el("a", { href: "#/units?unit=" + enc(u.id) + "&project=" + enc(u.projectId) }, "← Show in graph")),
      el("h1", null, u.name),
      el("p", { class: "sub" }, kindLabel(u.kind), " in ", el("a", { href: "#/units?project=" + enc(u.projectId) }, p.name || u.projectId),
        u.location ? [" · ", el("span", { class: "mono", title: u.location.file }, loc(u.location))] : null,
        u.parent ? [" · inside ", unitLink(u.parent)] : null),
      p.supportLevel === "limited" ? el("div", { class: "notice warning" }, "This project's language has limited support. It is shown as a single node and every effect found in it is attributed to the project as a whole.") : null,
      children.length ? [el("h2", null, "Contains"), el("ul", null, children.map(function (c) { return el("li", null, unitLink(c.id)); }))] : null,
      el("h2", null, "Depends on (" + out.length + ")"), depTable(out, "toUnit"),
      el("h2", null, "Used by (" + inc.length + ")"), depTable(inc, "fromUnit"),
      el("h2", null, "Entry points (" + (entryByUnit[u.id] || []).length + ")"), entryTable(entryByUnit[u.id] || [], false),
      el("h2", null, "Exit points (" + (exitByUnit[u.id] || []).length + ")"), exitTable(exitByUnit[u.id] || [], false)
    ]);
    main.appendChild(page);
  }

  // ---------- Entry points ----------
  function routeLabel(e) {
    if (e.route) return (e.httpMethod ? e.httpMethod + " " : "") + e.route;
    if (e.predicates.length) return (e.httpMethod ? e.httpMethod + " " : "") + e.predicates.map(predicateText).join(", ");
    return "(unresolved)";
  }
  function predicateText(p) {
    var subject = p.part === "path-segment" ? "segment " + p.index : p.part;
    var op = { "equals": "=", "not-equals": "≠", "starts-with": "starts with", "ends-with": "ends with", "contains": "contains", "matches": "matches", "parameter": "is parameter" }[p.operator] || p.operator;
    return subject + " " + op + (p.operator === "parameter" ? "" : " \"" + p.value + "\"");
  }

  function entryTable(list, withApp) {
    if (!list.length) return el("p", { class: "muted" }, "None found.");
    return el("div", { class: "table-wrap" }, el("table", null,
      el("thead", null, el("tr", null, withApp ? el("th", null, "Application") : null, el("th", null, "Route"), el("th", null, "Handler"), el("th", null, "Completeness"), el("th", null, "Reaches"), el("th", null, ""))),
      el("tbody", null, list.map(function (e) {
        var t = traceByEntry[e.id];
        return el("tr", null,
          withApp ? el("td", null, projectName(e.applicationId)) : null,
          el("td", { class: "mono" }, routeLabel(e)),
          el("td", null, unitLink(e.handlerUnit), el("div", { class: "muted mono", title: e.handlerMember }, shortMember(e.handlerMember))),
          el("td", null, el("span", { class: "tag " + e.completeness }, e.completeness), e.forms.length ? el("div", { class: "muted" }, e.forms.join(", ")) : null),
          el("td", null, t && t.exitPointIds.length ? summarizeCategories(t.exitPointIds.map(function (id) { return exitById[id]; }).filter(Boolean)) : el("span", { class: "muted" }, "—")),
          el("td", null, el("a", { href: "#/evidence/entry:" + enc(e.id) }, "Evidence")));
      }))));
  }

  function summarizeCategories(exits) {
    var counts = {};
    exits.forEach(function (x) { if (visibleExit(x)) counts[x.category] = (counts[x.category] || 0) + 1; });
    var keys = Object.keys(counts).sort();
    if (!keys.length) return el("span", { class: "muted" }, "—");
    return keys.map(function (k) { return el("span", { class: "tag", style: "margin-right:4px" }, (CATEGORY_LABEL[k] || k) + " " + counts[k]); });
  }

  function filterBar(state, options, onChange) {
    var search = el("input", { type: "search", placeholder: "Search…", "aria-label": "Search", value: state.q || "", on: { input: function () { state.q = search.value; onChange(); } } });
    var bar = el("div", { class: "toolbar" }, search);
    (options || []).forEach(function (o) {
      var b = el("button", { type: "button", "aria-pressed": String(!!state.on[o.id]), on: { click: function () { state.on[o.id] = !state.on[o.id]; b.setAttribute("aria-pressed", String(!!state.on[o.id])); onChange(); } } }, o.label);
      bar.appendChild(b);
    });
    return bar;
  }

  function matches(q, fields) {
    if (!q) return true;
    q = q.toLowerCase();
    return fields.some(function (f) { return f && String(f).toLowerCase().indexOf(q) >= 0; });
  }

  function entryPoints() {
    var page = el("div", { class: "page" });
    var state = { q: "", on: {} };
    var levels = [{ id: "complete", label: "Complete" }, { id: "partial", label: "Partial" }, { id: "unresolved", label: "Unresolved" }];
    var host = el("div");
    function draw() {
      var anyLevel = levels.some(function (l) { return state.on[l.id]; });
      var list = D.entryPoints.filter(function (e) {
        return (!anyLevel || state.on[e.completeness]) && matches(state.q, [routeLabel(e), unitName(e.handlerUnit), e.handlerMember, shortMember(e.handlerMember), projectName(e.applicationId)]);
      });
      host.textContent = "";
      host.appendChild(el("p", { class: "muted" }, plural(list.length, "entry point") + " shown."));
      host.appendChild(entryTable(list, true));
    }
    append(page, [
      el("h1", null, "Entry points"),
      el("p", { class: "sub" }, "Code reachable from an inbound-connection primitive that receives request data. Routes are reconstructed from the conditions on that data; nothing is guessed."),
      filterBar(state, levels, draw), host
    ]);
    draw();
    main.appendChild(page);
  }

  // ---------- Exit points ----------
  function exitTable(list, withProject) {
    if (!list.length) return el("p", { class: "muted" }, "None found.");
    return el("div", { class: "table-wrap" }, el("table", null,
      el("thead", null, el("tr", null, el("th", null, "Category"), withProject ? el("th", null, "Project") : null, el("th", null, unitWord.one), el("th", null, "Payload"), el("th", null, "Via"), el("th", null, "Confidence"), el("th", null, "Location"), el("th", null, ""))),
      el("tbody", null, list.map(function (x) {
        var u = units[x.unit] || {};
        return el("tr", null,
          el("td", null, el("span", { class: "tag" }, CATEGORY_LABEL[x.category] || x.category), el("div", { class: "muted mono", title: x.primitive }, shortMember(x.primitive))),
          withProject ? el("td", null, projectName(u.projectId)) : null,
          el("td", null, unitLink(x.unit), el("div", { class: "muted mono", title: x.member }, shortMember(x.member))),
          el("td", { class: "mono" }, x.payload ? (x.payload.value !== null ? x.payload.value : el("span", { class: "muted" }, x.payload.kind)) : el("span", { class: "muted" }, "—")),
          el("td", null, x.viaAssembly || el("span", { class: "muted" }, "direct")),
          el("td", null, el("span", { class: "tag confidence-" + x.confidence, title: "How directly the effect was established; long paths through dependencies are low" }, x.confidence)),
          el("td", { class: "mono" }, loc(x.location)),
          el("td", null, el("a", { href: "#/evidence/exit:" + enc(x.id) }, "Evidence")));
      }))));
  }

  function exitPoints() {
    var page = el("div", { class: "page" });
    var present = {};
    D.exitPoints.forEach(function (x) { present[x.category] = true; });
    var cats = Object.keys(CATEGORY_LABEL).filter(function (c) { return present[c]; }).map(function (c) { return { id: c, label: CATEGORY_LABEL[c] }; });
    var state = { q: "", on: {} };
    var lowCount = D.exitPoints.filter(function (x) { return HIDDEN_CONFIDENCE[x.confidence]; }).length;
    var options = cats.slice();
    if (lowCount) options.push({ id: "__low", label: "Low confidence (" + lowCount + ")" });
    var host = el("div");
    function draw() {
      var anyCat = cats.some(function (c) { return state.on[c.id]; });
      var list = D.exitPoints.filter(function (x) {
        var catOk = anyCat ? state.on[x.category] : !HIDDEN_BY_DEFAULT[x.category];
        var confOk = state.on.__low || !HIDDEN_CONFIDENCE[x.confidence];
        var u = units[x.unit] || {};
        return catOk && confOk && matches(state.q, [x.category, unitName(x.unit), x.member, x.primitive, x.viaAssembly, x.payload && x.payload.value, projectName(u.projectId)]);
      });
      host.textContent = "";
      var hiddenCats = Object.keys(HIDDEN_BY_DEFAULT).map(function (c) { return CATEGORY_LABEL[c] || c; });
      var hidden = D.exitPoints.filter(function (x) { return HIDDEN_BY_DEFAULT[x.category]; }).length;
      var notes = [];
      if (!anyCat && hidden) notes.push(hidden + " results in " + hiddenCats.join(" and ") + " are hidden; select those categories to see them.");
      if (!state.on.__low && lowCount) notes.push(lowCount + " low-confidence results (long paths through dependencies) are hidden.");
      host.appendChild(el("p", { class: "muted" }, plural(list.length, "exit point") + " shown. " + notes.join(" ")));
      host.appendChild(exitTable(list, true));
    }
    append(page, [
      el("h1", null, "Exit points"),
      el("p", { class: "sub" }, "Calls from user code that reach a side-effect primitive of .NET, directly or through a dependency."),
      filterBar(state, options, draw), host
    ]);
    draw();
    main.appendChild(page);
  }

  // ---------- Evidence ----------
  function evidence(r) {
    var page = el("div", { class: "page evidence" });
    var arg = r.arg || "";
    var kind = arg.slice(0, arg.indexOf(":"));
    var id = arg.slice(arg.indexOf(":") + 1);
    var item = kind === "entry" ? entryById[id] : exitById[id];
    if (!item) { append(page, el("p", { class: "empty" }, "Unknown result: " + arg)); main.appendChild(page); return; }
    var title = kind === "entry" ? routeLabel(item) : (CATEGORY_LABEL[item.category] || item.category) + " in " + unitName(item.unit);
    append(page, [
      el("p", { class: "muted" }, el("a", { href: kind === "entry" ? "#/entry-points" : "#/exit-points" }, "← Back")),
      el("h1", { class: kind === "entry" ? "mono" : null }, title),
      el("p", { class: "sub" }, kind === "entry" ? ["Handled by ", unitLink(item.handlerUnit), " · ", el("span", { class: "tag " + item.completeness }, item.completeness)] : ["Rule ", el("code", null, item.ruleId), " · primitive ", el("code", null, item.primitive)])
    ]);
    if (kind === "entry" && item.predicates.length) {
      append(page, [el("h2", null, "Conditions on the request"), el("ul", null, item.predicates.map(function (p) { return el("li", null, el("code", null, predicateText(p)), p.location ? el("span", { class: "muted" }, " at " + loc(p.location)) : null); }))]);
    }
    append(page, el("h2", null, kind === "entry" ? "How the request reaches the handler" : "Call path from user code to the primitive"));
    append(page, item.evidence.steps.length ? el("ol", null, item.evidence.steps.map(function (s) {
      return el("li", null, s.description, el("div", { class: "muted mono", title: s.member }, readableMember(s.member, false), s.assembly ? " [" + s.assembly + "]" : "", s.location ? " — " + loc(s.location) : ""));
    })) : el("p", { class: "muted" }, "No steps recorded."));
    if (kind === "entry") {
      var t = traceByEntry[item.id];
      var exits = t ? t.exitPointIds.map(function (x) { return exitById[x]; }).filter(Boolean) : [];
      append(page, [el("h2", null, "Side-effects this entry point can cause (" + exits.length + ")"), exitTable(exits, true)]);
    }
    main.appendChild(page);
  }

  // ---------- Diagnostics ----------
  function diagnostics() {
    var page = el("div", { class: "page" });
    var order = { error: 0, warning: 1, info: 2 };
    var list = D.diagnostics.slice().sort(function (a, b) { return order[a.severity] - order[b.severity]; });
    append(page, [
      el("h1", null, "Diagnostics"),
      el("p", { class: "sub" }, "Blind spots, unresolved items and warnings. Everything Floorplan could not analyze is listed here instead of being hidden."),
      list.length ? el("div", { class: "table-wrap" }, el("table", null,
        el("thead", null, el("tr", null, el("th", null, "Severity"), el("th", null, "Code"), el("th", null, "Message"), el("th", null, "Where"))),
        el("tbody", null, list.map(function (d) {
          return el("tr", null, el("td", null, el("span", { class: "tag" }, d.severity)), el("td", { class: "mono" }, d.code),
            el("td", { style: "white-space:pre-wrap" }, d.message), el("td", { class: "mono" }, d.projectId ? projectName(d.projectId) : "", d.location ? " " + loc(d.location) : ""));
        })))) : el("p", { class: "empty" }, "No diagnostics.")
    ]);
    main.appendChild(page);
  }

  route();
})();
