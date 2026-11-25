// Floorplan website. Plain JavaScript, no libraries, no network. Reads window.FLOORPLAN_DATA (the analysis
// model) and nothing else. Labels come from the model: a unit's kind decides whether it is called a module,
// namespace, type or project.
(function () {
  "use strict";

  // A folder of solutions analyzed as one landscape (docs/landscape-plan.md) has pages of its own and shows its C4
  // model on the Architecture page; each solution's site, under solutions/<slug>/ (or in the same file, with
  // --single-file), links back to it.
  var LANDSCAPE = window.FLOORPLAN_LANDSCAPE || null;
  var LANDSCAPE_LINK = window.FLOORPLAN_LANDSCAPE_LINK || null;
  var D = window.FLOORPLAN_DATA || landscapeModel(LANDSCAPE);
  var main = document.getElementById("main");

  // The landscape as an analysis model with nothing but its C4 model and diagnostics, for the pages they share.
  function landscapeModel(L) {
    var graph = { width: 0, height: 0, nodes: [], edges: [] };
    return {
      toolVersion: L.toolVersion, generatedAt: L.generatedAt, solution: { name: L.name, path: L.path },
      projects: [], units: [], dependencies: [], typeDependencies: [], entryPoints: [], exitPoints: [], traces: [],
      layout: { projectGraph: graph, unitGraph: graph, projectBoxes: [], projectEdges: [], detachedBoxes: [], flowGraph: graph },
      presentation: { hiddenCategories: ["environment", "nondeterminism"], hiddenConfidence: ["low"] },
      architecture: L.architecture, diagnostics: L.diagnostics
    };
  }
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
  // A page of a solution's site (slug), or of the landscape's (null). Written as one file (--single-file), the
  // landscape holds every site, each opened as the file with ?solution=<slug>; otherwise solutions/<slug>/ holds one.
  function siteHref(slug, hash) {
    if (window.FLOORPLAN_SINGLE_FILE) return (location.pathname.split("/").pop() || "") + (slug ? "?solution=" + enc(slug) : "") + hash;
    if (LANDSCAPE_LINK) return (slug ? "../" + enc(slug) + "/" : "../../") + "index.html" + hash;
    return "solutions/" + enc(slug) + "/index.html" + hash;
  }

  var KIND_LABEL = { module: ["Module", "Modules"], namespace: ["Namespace", "Namespaces"], type: ["Type", "Types"], folder: ["Folder", "Folders"], project: ["Project", "Projects"] };
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
    "native": "Native", "environment": "Environment", "nondeterminism": "Nondeterminism", "background": "Background"
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

  // A unit's full name; the global namespace has none and is shown by its label.
  function fullName(u) { return u.name || u.displayName; }
  function unitName(id) { var u = units[id]; return u ? fullName(u) : id; }
  function unitLink(id) {
    var u = units[id];
    if (!u) return el("span", { class: "mono" }, id);
    return el("a", { href: "#/unit/" + enc(id), title: fullName(u) }, fullName(u));
  }
  function projectName(id) { var p = projects[id]; return p ? p.name : id; }
  // A member as people wrote it. The compiler turns a lambda into a class nested in its module, named after the
  // enclosing binding and the line ("map@24", "Pipe #1 input at line 24@24"), with the body in Invoke (or
  // MoveNext for a task or async body): shown as "Admin.map › lambda at line 24". C# names a lambda after its
  // enclosing method ("<Map>b__0_1" in "Admin/<>c" or "Admin/<>c__DisplayClass0_0"), an async body after its
  // method ("Admin/<Load>d__3", MoveNext) and a local function "<Map>g__Name|0_0": shown as "Admin › lambda in Map".
  function readableMember(m, short) {
    if (!m) return "";
    var i = m.lastIndexOf("::");
    var type = i >= 0 ? m.slice(0, i) : m, method = i >= 0 ? m.slice(i + 2) : "";
    var owner = function (t) { return short ? t.split(".").pop().replace(/\+/g, ".") : t; };
    var cs = readableCSharp(type, method, owner);
    if (cs) return cs;
    var slash = type.indexOf("/"), last = type.slice(type.lastIndexOf("/") + 1), at = last.match(/@(\d+)(?:-\d+)?$/);
    if (slash >= 0 && at) {
      var binding = last.match(/^([A-Za-z_][\w']*)@\d+/);
      var what = method === "MoveNext" ? "async body" : "lambda";
      return owner(type.slice(0, slash)) + (binding ? "." + binding[1] : "") + " › " + what + " at line " + at[1];
    }
    return owner(type) + (i >= 0 ? (short ? "." : "::") + method : "");
  }
  function readableCSharp(type, method, owner) {
    var enclosing = function (n) { return n === "<Main>$" ? "top-level statements" : n; };
    var segs = type.split("/");
    var closure = /^<>c(__DisplayClass[\d_]+)?$/;
    var lambda = method.match(/^<(.+)>b__[\d_]+$/);
    if (lambda && segs.length > 1 && closure.test(segs[segs.length - 1]))
      return owner(segs.slice(0, -1).join("/")) + " › lambda in " + enclosing(lambda[1]);
    var local = method.match(/^<(.+)>g__(.+)\|[\d_]+$/);
    if (local) return owner(type) + " › " + local[2] + " in " + enclosing(local[1]);
    var async = segs.length > 1 && method === "MoveNext" && segs[segs.length - 1].match(/^<(.+)>d(__\d+)?$/);
    if (async) {
      var outer = segs.slice(0, -1);
      if (outer.length > 1 && closure.test(outer[outer.length - 1])) outer = outer.slice(0, -1);
      var inner = async[1].match(/^<(.+)>b__[\d_]+$/);
      return owner(outer.join("/")) + (inner ? " › async lambda in " + enclosing(inner[1]) : " › async body of " + enclosing(async[1]));
    }
    return null;
  }
  function shortMember(m) { return readableMember(m, true); }

  // ---------- Navigation ----------
  var VIEWS = [
    { id: "overview", label: "Overview" },
    { id: "projects", label: "Projects" },
    { id: "units", label: unitWord.many },
    { id: "flow", label: "Flow" },
    { id: "architecture", label: "Architecture" },
    { id: "entry-points", label: "Entry points", count: function () { return D.entryPoints.length; } },
    { id: "exit-points", label: "Exit points", count: function () { return D.exitPoints.filter(visibleExit).length; } },
    { id: "diagnostics", label: "Diagnostics", count: function () { return D.diagnostics.filter(function (d) { return d.severity !== "info"; }).length; } }
  ];

  if (LANDSCAPE) {
    VIEWS = [
      { id: "solutions", label: "Solutions", count: function () { return LANDSCAPE.solutions.length; } },
      { id: "architecture", label: "Landscape" },
      { id: "interactions", label: "Interactions", count: function () { return LANDSCAPE.interactions.length; } },
      VIEWS[VIEWS.length - 1]
    ].filter(function (v) { return v.id !== "architecture" || D.architecture.views.length; });
    document.title = LANDSCAPE.name + " · Floorplan";
  }

  // In a solution's site that is part of a landscape: the way back to it, and to the other solutions' sites (the
  // page shown is kept).
  if (LANDSCAPE_LINK) {
    var current = LANDSCAPE_LINK.solutions.filter(function (s) { return s.slug === LANDSCAPE_LINK.current; })[0];
    var switcher = el("select", { "aria-label": "Solution" }, LANDSCAPE_LINK.solutions.map(function (s) {
      return el("option", { value: s.slug, selected: s.slug === LANDSCAPE_LINK.current ? "selected" : null }, s.name);
    }));
    switcher.addEventListener("change", function () { location.href = siteHref(switcher.value, location.hash); });
    var nav0 = document.getElementById("nav");
    nav0.parentNode.insertBefore(el("div", { class: "landscape-bar" },
      el("a", { href: siteHref(null, "#/solutions"), title: "Every solution in " + LANDSCAPE_LINK.landscape }, "◂ Solutions"),
      switcher), nav0);
    document.title = (current ? current.name + " · " : "") + LANDSCAPE_LINK.landscape + " · Floorplan";
  }

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
      "overview": overview, "projects": projectGraph, "units": unitGraph, "flow": flowGraph, "unit": unitDetail, "architecture": architecture,
      "entry-points": entryPoints, "exit-points": exitPoints, "evidence": evidence, "diagnostics": diagnostics
    }[r.view] || overview;
    var navId = r.view === "unit" ? "units" : (r.view === "evidence" ? null : r.view);
    if (LANDSCAPE) {
      view = { "architecture": architecture, "interactions": interactions, "diagnostics": diagnostics }[r.view] || solutions;
      if (view === architecture && !D.architecture.views.length) view = solutions;
      navId = view === solutions ? "solutions" : r.view;
    }
    renderNav(navId);
    if (viewLeave) viewLeave();
    viewKeys = null;
    viewLeave = null;
    main.textContent = "";
    view(r);
    main.focus({ preventScroll: true });
  }
  window.addEventListener("hashchange", route);

  // ---------- Architecture (C4) ----------
  // The C4 model of the solution (docs/c4-plan.md), in four levels: the system in its context, its containers
  // (applications), each container's components (units) and each component's code (types). Every view is laid
  // out by the core; a view of an element draws a boundary around the element's parts.
  var C4 = D.architecture || { elements: [], relationships: [], views: [] };
  var c4Elements = by(C4.elements, "id");
  var c4Views = by(C4.views, "id");
  var c4ViewOf = {};
  C4.views.forEach(function (v) { if (v.level !== "context") c4ViewOf[v.scope] = v; });
  var C4_KIND = {
    "person": "Person", "software-system": "Software system", "external-system": "External system",
    "container": "Container", "component": "Component", "code": "Code"
  };
  // More labelled lines than this in one view, and labels show only on selection or hover.
  var C4_QUIET_LABELS = 24;
  var C4_LEVEL = { "context": "System context", "containers": "Containers", "components": "Components", "code": "Code" };
  // A landscape's views (docs/landscape-plan.md): its systems ("context", and "context-all" with every external
  // system), every system's containers in a boundary each ("containers", whose scope is the landscape), and each
  // system's containers with those it talks to. A container's components are in its solution's site.
  var C4_ALL_SCOPE = "landscape";
  if (LANDSCAPE) C4_LEVEL.context = "Landscape";
  // The relationships matched by route only (probable), by their sources.
  var c4Probable = {};
  if (LANDSCAPE) LANDSCAPE.interactions.forEach(function (i) { if (i.probable) i.sources.forEach(function (s) { c4Probable[s] = true; }); });
  function c4IsProbable(rel) { return LANDSCAPE && rel.sources.length > 0 && rel.sources.every(function (s) { return c4Probable[s]; }); }
  // Where an element of a landscape is shown in full: a system's or a container's architecture in its solution's site.
  function c4SiteOf(e) {
    if (!e) return null;
    // In a solution's site: another system of its landscape, in that system's site.
    var other = LANDSCAPE_LINK && /^external:system:(.+)$/.exec(e.id);
    if (other) return { href: siteHref(other[1], "#/architecture"), solution: e.name };
    if (!LANDSCAPE) return null;
    var m = /^(system|container):([^\/]+)(?:\/(.*))?$/.exec(e.id);
    if (!m) return null;
    var page = m[1] === "container" ? "architecture/components:" + enc(m[3]) : "architecture";
    return { href: siteHref(m[2], "#/" + page), solution: systemName("system:" + m[2]) };
  }

  // The views from the system down to this one: context, containers, a container's components, a component's code.
  function c4Trail(v) {
    var trail = [];
    if (LANDSCAPE) {
      trail.push({ view: c4Views.context, label: C4_LEVEL.context });
      if (v.id === "context-all") trail.push({ view: v, label: "Every external system" });
      else if (v.scope === C4_ALL_SCOPE && v.level !== "context") trail.push({ view: v, label: "Every system's containers" });
      else if (v.level !== "context") trail.push({ view: v, label: systemName(v.scope) });
      return trail.filter(function (t) { return t.view; });
    }
    var scope = v.level === "context" ? null : c4Elements[v.scope];
    if (v.level === "code" && scope) {
      var container = c4Elements[scope.parent];
      trail.unshift({ view: v, label: scope.name });
      if (container && c4ViewOf[container.id]) trail.unshift({ view: c4ViewOf[container.id], label: container.name });
    } else if (v.level === "components" && scope) {
      trail.unshift({ view: v, label: scope.name });
    }
    if (v.level !== "context") trail.unshift({ view: c4Views["containers"], label: C4_LEVEL.containers });
    trail.unshift({ view: c4Views["context"], label: C4_LEVEL.context });
    return trail.filter(function (t) { return t.view; });
  }

  // Text wrapped to lines of at most `max` characters, at most `lines` of them.
  function wrapText(text, max, lines) {
    var words = String(text || "").split(/\s+/), out = [], line = "";
    words.forEach(function (w) {
      if (!w) return;
      if ((line + " " + w).trim().length > max && line) { out.push(line); line = w; }
      else line = (line + " " + w).trim();
    });
    if (line) out.push(line);
    if (out.length > lines) { out = out.slice(0, lines); out[lines - 1] = nodeText(out[lines - 1] + " …", max * 7.2 + 16); }
    return out;
  }

  // A view's link; `select` names an element to select and centre when it opens.
  function c4Link(viewId, select) { return "#/architecture/" + enc(viewId) + (select ? "?select=" + enc(select) : ""); }

  // ---------- Architecture navigation ----------
  // Moving between levels: the level switcher, "up" and the sidebar's tree of every view.
  var C4_LEVELS = ["context", "containers", "components", "code"];
  function c4ByName(a, b) { return a.name.localeCompare(b.name) || (a.id < b.id ? -1 : 1); }
  // The container a view lies in: a components view's scope, or a code view's component's container.
  function c4ContainerOf(v) {
    if (LANDSCAPE) return v.level === "containers" && v.scope !== C4_ALL_SCOPE ? v.scope : null;
    if (v.level === "components") return v.scope;
    if (v.level === "code") { var c = c4Elements[v.scope]; return c ? c.parent : null; }
    return null;
  }
  var c4Containers = C4.elements.filter(function (e) { return (LANDSCAPE ? e.kind === "software-system" : e.kind === "container") && c4ViewOf[e.id]; }).sort(c4ByName);
  // The view one level up, and the element of it that this view opened from.
  function c4Up(v) {
    if (LANDSCAPE) return v.id !== "context" && c4Views.context ? { view: c4Views.context, select: c4Elements[v.scope] ? v.scope : null } : null;
    if (v.level === "code") { var c = c4Elements[v.scope]; var cv = c && c4ViewOf[c.parent]; return cv ? { view: cv, select: v.scope } : null; }
    if (v.level === "components") return c4Views.containers ? { view: c4Views.containers, select: v.scope } : null;
    if (v.level === "containers") return c4Views.context ? { view: c4Views.context, select: "system" } : null;
    return null;
  }
  // The view at `level` nearest to view v. Going down needs a choice of element: the selected one, else the
  // only one there is; without one, there is no view to go to and the reason says what to choose.
  function c4ViewAt(level, v, selected) {
    if (level === v.level) return { view: v };
    var sel = selected && c4Elements[selected];
    var down = C4_LEVELS.indexOf(level) > C4_LEVELS.indexOf(v.level);
    if (!down) {
      var up = c4Up(v);
      while (up && up.view.level !== level) up = c4Up(up.view);
      return up || { reason: "No " + C4_LEVEL[level].toLowerCase() + " view" };
    }
    if (LANDSCAPE) {
      if (level !== "containers") return { reason: "A container's components and code are in its solution's site: open the container" };
      var system = sel && sel.kind === "software-system" ? sel.id : sel && sel.kind === "container" ? sel.parent : null;
      return system && c4ViewOf[system] ? { view: c4ViewOf[system] } : c4Views.containers ? { view: c4Views.containers } : { reason: "No containers view" };
    }
    if (level === "containers") return c4Views.containers ? { view: c4Views.containers } : { reason: "No containers view" };
    if (level === "components") {
      var container = sel && sel.kind === "container" ? sel.id : (c4Containers.length === 1 ? c4Containers[0].id : null);
      return container && c4ViewOf[container] ? { view: c4ViewOf[container] }
        : { reason: "Select a container first, or choose one in the sidebar" };
    }
    // Code: the selected component's, or from a container view the only component there is.
    if (sel && sel.kind === "component" && c4ViewOf[sel.id]) return { view: c4ViewOf[sel.id] };
    return { reason: v.level === "components" ? "Select a component first, or choose one in the sidebar" : "Choose a component first" };
  }

  // Sidebar state, kept across views: whether it shows, which containers are expanded, the filter.
  var c4NavKey = "floorplan-c4-nav";
  var c4NavShown = (function () { try { var s = localStorage.getItem(c4NavKey); if (s) return s === "shown"; } catch (e) { /* private mode */ } return !window.matchMedia || !matchMedia("(max-width: 900px)").matches; })();
  var c4Expanded = {};
  var c4Filter = "";
  // Components of a container, grouped by the project they come from: the container's own project first.
  function c4ComponentGroups(containerId) {
    var comps = C4.elements.filter(function (e) { return (LANDSCAPE ? e.kind === "container" : e.kind === "component") && e.parent === containerId; });
    if (LANDSCAPE) return [{ project: "", name: "", components: comps.sort(c4ByName) }];
    var projectOf = function (e) { var u = e.sources.map(function (s) { return units[s]; }).filter(Boolean)[0]; return u ? u.projectId : ""; };
    var groups = groupBy(comps, projectOf);
    var own = (c4Elements[containerId] || {}).name;
    return Object.keys(groups).sort(function (a, b) {
      var ao = projectName(a) === own ? 0 : 1, bo = projectName(b) === own ? 0 : 1;
      return ao - bo || projectName(a).localeCompare(projectName(b));
    }).map(function (p) { return { project: p, name: p ? projectName(p) : "Other", components: groups[p].sort(c4ByName) }; });
  }

  function c4Sidebar(v) {
    var aside = el("aside", { class: "c4-nav", "aria-label": "Architecture views", hidden: !c4NavShown });
    var tree = el("div", { class: "c4-tree" });
    var current = c4ContainerOf(v);
    if (current) c4Expanded[current] = true;
    var item = function (view, label, extra, cls) {
      var here = view.id === v.id;
      return el("a", { class: "c4-item" + (cls ? " " + cls : ""), href: c4Link(view.id), "aria-current": here ? "page" : null, title: label },
        el("span", { class: "c4-item-name" }, label), extra ? el("span", { class: "count" }, extra) : null);
    };
    function draw() {
      tree.textContent = "";
      var q = c4Filter.trim().toLowerCase();
      if (q) {
        // Every view whose element's name matches, at most a few hundred.
        var hits = C4.views.filter(function (w) {
          var e = w.level === "context" ? null : c4Elements[w.scope];
          return (e ? e.name : C4_LEVEL[w.level]).toLowerCase().indexOf(q) >= 0;
        });
        if (!hits.length) { tree.appendChild(el("p", { class: "muted c4-none" }, "No view matches.")); return; }
        tree.appendChild(el("ul", { class: "plain" }, hits.slice(0, 300).map(function (w) {
          var e = c4Elements[w.scope];
          var where = w.level === "code" && e ? (c4Elements[e.parent] || {}).name : null;
          return el("li", null, item(w, w.level === "context" || w.level === "containers" ? C4_LEVEL[w.level] : e.name,
            C4_LEVEL[w.level] + (where ? " · " + where : ""), "c4-hit"));
        }), hits.length > 300 ? el("li", { class: "muted" }, "… and " + (hits.length - 300) + " more; type more to narrow.") : null));
        return;
      }
      var list = el("ul", { class: "plain" });
      if (c4Views.context) list.appendChild(el("li", null, item(c4Views.context, "1 · " + C4_LEVEL.context)));
      if (c4Views["context-all"]) list.appendChild(el("li", null, item(c4Views["context-all"], "1 · With every external system")));
      if (c4Views.containers) list.appendChild(el("li", null, item(c4Views.containers, "2 · " + (LANDSCAPE ? "Every system's containers" : C4_LEVEL.containers))));
      c4Containers.forEach(function (c) {
        var open = !!c4Expanded[c.id];
        var groups = open ? c4ComponentGroups(c.id) : null;
        var count = C4.elements.filter(function (e) { return (LANDSCAPE ? e.kind === "container" : e.kind === "component") && e.parent === c.id; }).length;
        var toggle = function () { c4Expanded[c.id] = !c4Expanded[c.id]; draw(); };
        var li = el("li", { class: "c4-branch" },
          el("div", { class: "c4-row" },
            el("button", { type: "button", class: "c4-twisty ghost", "aria-expanded": String(open), "aria-label": (open ? "Collapse " : "Expand ") + c.name, on: { click: toggle } }, open ? "▾" : "▸"),
            item(c4ViewOf[c.id], c.name, String(count), "c4-container-item")));
        if (open) {
          li.appendChild(el("ul", { class: "plain c4-children" }, groups.map(function (gr) {
            return [groups.length > 1 ? el("li", { class: "c4-group", title: "Components from the project " + gr.name }, gr.name) : null,
              gr.components.map(function (comp) {
                var cv = c4ViewOf[comp.id];
                var site = c4SiteOf(comp);
                return el("li", null, cv ? item(cv, comp.name)
                  : site ? el("a", { class: "c4-item", href: site.href, title: "Open in " + site.solution + "'s site" }, el("span", { class: "c4-item-name" }, comp.name + " ↗"))
                  : el("span", { class: "c4-item muted", title: comp.name }, comp.name));
              })];
          })));
        }
        list.appendChild(li);
      });
      tree.appendChild(list);
    }
    var filter = el("input", { type: "search", class: "c4-filter", placeholder: "Filter views…", "aria-label": "Filter views", value: c4Filter,
      on: {
        input: function (ev) { c4Filter = ev.target.value; draw(); },
        keydown: function (ev) {
          if (ev.key === "Enter") { var a = tree.querySelector("a.c4-item"); if (a) location.hash = a.getAttribute("href"); }
          else if (ev.key === "Escape") { c4Filter = ""; ev.target.value = ""; draw(); }
        }
      } });
    append(aside, [filter, tree]);
    draw();
    aside.reveal = function () { var a = tree.querySelector("[aria-current=page]"); if (a && a.scrollIntoView) a.scrollIntoView({ block: "nearest" }); };
    aside.filter = filter;
    return aside;
  }

  // Keys of the view on screen; a view sets its handler when drawn and route() clears it.
  var viewKeys = null;
  // Called when the view on screen is left, before the next one is drawn.
  var viewLeave = null;
  // Where each Architecture view was looked at last, so coming back to it (up, back) shows the same place.
  var c4Viewport = {};
  document.addEventListener("keydown", function (ev) {
    if (!viewKeys || ev.ctrlKey || ev.metaKey || ev.altKey) return;
    var t = ev.target;
    if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.isContentEditable)) return;
    viewKeys(ev);
  });

  function architecture(r) {
    var v = c4Views[r.arg || "context"] || c4Views["context"];
    var page = el("div", { class: "graph-page" });
    if (!v) {
      append(page, el("p", { class: "empty" }, "This model has no C4 views. Analyze the solution again with this version of Floorplan."));
      main.appendChild(page);
      return;
    }
    var g = v.layout;
    // Whether an element is one of the view's own: its scope's part (or, in a landscape's view of every system's
    // containers, any container).
    var c4Inside = function (e, id) { return e.parent === v.scope || id === v.scope || (LANDSCAPE && v.scope === C4_ALL_SCOPE && e.kind === "container"); };
    var host = el("div", { class: "graph" });
    var canvas = graphCanvas(g.width, g.height);
    var side = el("div", { class: "side c4-side", hidden: true });
    var nodes = by(g.nodes, "id");
    var shown = {};
    v.elements.forEach(function (id) { shown[id] = true; });

    // The boundary of the view's scope, around the elements that are its parts; in a landscape's view of every
    // system's containers, one around each system's.
    var boundaries = v.level === "context" ? [] : LANDSCAPE && v.scope === C4_ALL_SCOPE
      ? Object.keys(groupBy(g.nodes.filter(function (n) { var e = c4Elements[n.id]; return e && e.kind === "container"; }), function (n) { return c4Elements[n.id].parent; }))
      : [v.scope];
    boundaries.forEach(function (scopeId) {
      var parts = g.nodes.filter(function (n) { var e = c4Elements[n.id]; return e && e.parent === scopeId; });
      if (!parts.length) return;
      var pad = 28, head = 30;
      var x0 = Math.min.apply(null, parts.map(function (n) { return n.x; })) - pad;
      var y0 = Math.min.apply(null, parts.map(function (n) { return n.y; })) - pad - head;
      var x1 = Math.max.apply(null, parts.map(function (n) { return n.x + n.width; })) + pad;
      var y1 = Math.max.apply(null, parts.map(function (n) { return n.y + n.height; })) + pad;
      var scope = c4Elements[scopeId] || { name: scopeId, kind: "software-system" };
      canvas.world.appendChild(svg("g", { class: "c4-boundary" },
        svg("rect", { x: x0, y: y0, width: x1 - x0, height: y1 - y0, rx: 10 }),
        svg("text", { x: x0 + 12, y: y0 + 20, text: scope.name + " [" + (C4_KIND[scope.kind] || scope.kind) + "]" })));
    });

    var edgeEls = [], nodeEls = {};
    // Layers: lines under elements, then a veil and, above it, what a selection or a search brings forward.
    // Dimming the rest with the veil costs the same at any size, where fading each element costs per element.
    var edgesLayer = svg("g"), nodesLayer = svg("g");
    var veil = svg("rect", { class: "c4-veil", x: -1e5, y: -1e5, width: 2e5, height: 2e5, display: "none" });
    var frontEdges = svg("g"), frontNodes = svg("g");
    [edgesLayer, nodesLayer, veil, frontEdges, frontNodes].forEach(function (l) { canvas.world.appendChild(l); });
    var edgesOfNode = {}, relatedOf = {};
    // A view with many labelled lines shows its labels only for the selected element's lines and the line under
    // the pointer, or all of them on request: drawn at once they crowd one another.
    var labelled = groupBy(v.relationships.filter(function (rel) { return rel.description !== "uses"; }), function (rel) { return rel.from + ">" + rel.to; });
    var quiet = Object.keys(labelled).length > C4_QUIET_LABELS;
    if (quiet) canvas.world.classList.add("c4-quiet");
    var edgeOf = {};
    g.edges.forEach(function (e) { edgeOf[e.from + ">" + e.to] = e; });
    var relsByPair = groupBy(v.relationships, function (rel) { return rel.from + ">" + rel.to; });
    // Labels are placed one by one, each at the first spot along its line (the middle, then further out) where
    // it overlaps no label placed before it and no box: lines that meet at a box would otherwise put their
    // labels on one another.
    var taken = g.nodes.map(function (n) { return { x: n.x, y: n.y, w: n.width, h: n.height }; });
    var overlaps = function (a, b) { return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h; };
    var along = function (p, t) {
      var lengths = [], total = 0;
      for (var i = 1; i < p.length; i++) { var d = Math.hypot(p[i].x - p[i - 1].x, p[i].y - p[i - 1].y); lengths.push(d); total += d; }
      var at = total * t;
      for (var j = 0; j < lengths.length; j++) {
        if (at <= lengths[j] || j === lengths.length - 1) {
          var f = lengths[j] ? Math.min(1, at / lengths[j]) : 0;
          return { x: p[j].x + (p[j + 1].x - p[j].x) * f, y: p[j].y + (p[j + 1].y - p[j].y) * f };
        }
        at -= lengths[j];
      }
      return p[0];
    };
    var place = function (p, lines) {
      var w = Math.max.apply(null, lines.map(function (t) { return t.length; })) * 6.2 + 8, h = lines.length * 14 + 4;
      var box = function (c) { return { x: c.x - w / 2, y: c.y - (lines.length - 1) * 7 - 12, w: w, h: h }; };
      var tries = [0.5, 0.4, 0.6, 0.3, 0.7, 0.22, 0.78, 0.15, 0.85];
      for (var i = 0; i < tries.length; i++) {
        var c = along(p, tries[i]), b = box(c);
        if (!taken.some(function (o) { return overlaps(o, b); })) { taken.push(b); return c; }
      }
      var mid = along(p, 0.5);
      taken.push(box(mid));
      return mid;
    };
    Object.keys(relsByPair).forEach(function (key) {
      var e = edgeOf[key];
      if (!e) return;
      var rels = relsByPair[key];
      var p = e.points;
      // A dependency between components ("uses") is the common case: its arrow says it without a label.
      var label = rels.map(function (rel) { return rel.description; }).filter(function (d) { return d !== "uses"; }).join("; ");
      var tech = Array.from(new Set(rels.map(function (rel) { return rel.technology; }).filter(Boolean))).join(", ");
      var lines = label ? wrapText(label, 30, 2) : [];
      if (tech) lines.push("[" + nodeText(tech, 230) + "]");
      // Matched by route only: two deployments can run the same route, so the line is dashed and says so.
      var probable = rels.every(c4IsProbable);
      if (probable) lines.push("probable: route only");
      var mid = lines.length ? place(p, lines) : p[0];
      var group = svg("g", { class: "c4-rel" + (probable ? " c4-probable" : "") },
        svg("path", { class: "g-edge", d: curve(p) }),
        lines.length ? svg("text", { x: mid.x, y: mid.y - (lines.length - 1) * 7, "text-anchor": "middle" },
          lines.map(function (t, i) { return svg("tspan", { x: mid.x, dy: i === 0 ? 0 : 14, class: i === lines.length - 1 && tech ? "tech" : null, text: t }); })) : null,
        svg("title", { text: rels.map(function (rel) { return rel.description + (rel.technology ? " [" + rel.technology + "]" : "") + " — " + plural(rel.sources.length, "source"); }).join("\n") }));
      edgeEls.push(group);
      edgesLayer.appendChild(group);
      [rels[0].from, rels[0].to].forEach(function (end, i) {
        (edgesOfNode[end] = edgesOfNode[end] || []).push(group);
        var other = i ? rels[0].from : rels[0].to;
        (relatedOf[end] = relatedOf[end] || {})[other] = true;
      });
      // A line's box, with room for its label, for drawing only what is near the screen.
      var xs = p.map(function (q) { return q.x; }), ys = p.map(function (q) { return q.y; });
      canvas.track(group, Math.min.apply(null, xs) - 120, Math.min.apply(null, ys) - 40, Math.max.apply(null, xs) + 120, Math.max.apply(null, ys) + 40);
    });

    g.nodes.forEach(function (n) {
      var e = c4Elements[n.id];
      if (!e) return;
      var kind = C4_KIND[e.kind] || e.kind;
      var sub = "[" + kind + (e.technology ? ": " + e.technology : "") + "]";
      var below = c4ViewOf[e.id];
      var site = below ? null : c4SiteOf(e);
      var open = function () { if (below) location.hash = c4Link(below.id); else if (site) location.href = site.href; };
      var children = [];
      if (e.kind === "person") {
        children.push(svg("circle", { class: "c4-head", cx: n.x + n.width / 2, cy: n.y + 4, r: 18 }));
        children.push(svg("rect", { class: "c4-shape", x: n.x, y: n.y + 16, width: n.width, height: n.height - 16, rx: 28 }));
      } else {
        children.push(svg("rect", { class: "c4-shape", x: n.x, y: n.y, width: n.width, height: n.height, rx: e.kind === "code" ? 4 : 10 }));
      }
      var top = n.y + (e.kind === "person" ? 40 : 24);
      children.push(svg("text", { class: "c4-name", x: n.x + n.width / 2, y: top, "text-anchor": "middle", text: nodeText(e.name, n.width) }));
      children.push(svg("text", { class: "c4-kind", x: n.x + n.width / 2, y: top + 17, "text-anchor": "middle", text: nodeText(sub, n.width) }));
      wrapText(e.description, 34, e.kind === "person" ? 2 : 3).forEach(function (t, i) {
        children.push(svg("text", { class: "c4-desc", x: n.x + n.width / 2, y: top + 38 + i * 15, "text-anchor": "middle", text: t }));
      });
      if (below || site) children.push(svg("text", { class: "c4-open", x: n.x + n.width - 10, y: n.y + n.height - 8, "text-anchor": "end", text: below ? "open ›" : "open site ↗" }));
      children.push(svg("title", { text: e.name + " " + sub + (e.description ? "\n" + e.description : "") +
        (below ? "\nDouble-click to open its " + C4_LEVEL[below.level].toLowerCase() + "." : site ? "\nDouble-click to open it in " + site.solution + "'s site." : "") }));
      var node = svg("g", {
        class: "g-node c4-el c4-" + e.kind + (c4Inside(e, n.id) ? "" : " c4-outside"),
        tabindex: "0", role: "button", "aria-label": e.name + ", " + kind,
        on: {
          click: function () { select(n.id); },
          dblclick: open,
          keydown: function (ev) { if (ev.key === "Enter") { ev.preventDefault(); if (selected === n.id && (below || site)) open(); else select(n.id); } }
        }
      }, children);
      nodeEls[n.id] = node;
      nodesLayer.appendChild(node);
      canvas.track(node, n.x, n.y - 20, n.x + n.width, n.y + n.height);
    });

    // Brings these elements and lines in front of the veil, and sends back what was in front before.
    var inFront = [];
    function spotlight(ids, lines) {
      // Moving an element in the document takes its focus away; the element that had it gets it back.
      var active = document.activeElement;
      inFront.forEach(function (x) { (x.classList.contains("c4-rel") ? edgesLayer : nodesLayer).appendChild(x); x.classList.remove("lit", "found"); });
      inFront = [];
      if (!ids) { veil.setAttribute("display", "none"); ids = []; } else veil.removeAttribute("display");
      lines.forEach(function (x) { x.classList.add("lit"); frontEdges.appendChild(x); inFront.push(x); });
      ids.forEach(function (k) { var x = nodeEls[k]; if (x) { frontNodes.appendChild(x); inFront.push(x); } });
      if (active && active !== document.activeElement && active.isConnected && active.focus) active.focus({ preventScroll: true });
    }

    var selected = null;
    function select(id) {
      if (selected && nodeEls[selected]) nodeEls[selected].classList.remove("selected");
      selected = id;
      if (id && nodeEls[id]) nodeEls[id].classList.add("selected");
      spotlight(id ? [id].concat(Object.keys(relatedOf[id] || {})) : null, id ? edgesOfNode[id] || [] : []);
      renderSide(id);
      updateLevels();
    }

    function sourceLink(s) {
      if (LANDSCAPE) {
        var ls = landscapeSource(s), href = sourceHref(s);
        var solution = systemName("system:" + s.split(":")[0]);
        return ls && href ? el("a", { class: "mono", href: href }, solution + " · " + ls.label) : el("span", { class: "mono" }, s);
      }
      if (entryById[s]) return el("a", { class: "mono", href: "#/evidence/entry:" + enc(s) }, routeLabel(entryById[s]));
      if (exitById[s]) {
        var x = exitById[s];
        var text = x.payload && x.payload.value ? x.payload.value : shortMember(x.primitive);
        return el("a", { class: "mono", href: "#/evidence/exit:" + enc(s) }, (CATEGORY_LABEL[x.category] || x.category) + ": " + text);
      }
      if (units[s]) return unitLink(s);
      if (projects[s]) return el("a", { href: "#/units?project=" + enc(s) }, projectName(s));
      return el("span", { class: "mono" }, s);
    }

    function relList(title, rels, other) {
      if (!rels.length) return null;
      return [el("h4", null, title), el("ul", { class: "plain" }, rels.map(function (rel) {
        var o = c4Elements[other(rel)];
        return el("li", null, el("strong", null, o ? o.name : other(rel)), " — ", rel.description,
          rel.technology ? el("span", { class: "muted" }, " [" + rel.technology + "]") : null,
          el("ul", { class: "plain sources" }, rel.sources.slice(0, 6).map(function (s) { return el("li", null, sourceLink(s)); }),
            rel.sources.length > 6 ? el("li", { class: "muted" }, "… and " + (rel.sources.length - 6) + " more") : null));
      }))];
    }

    function renderSide(id) {
      side.textContent = "";
      var e = id && c4Elements[id];
      if (!e) { side.hidden = true; return; }
      side.hidden = false;
      var below = c4ViewOf[e.id];
      var site = c4SiteOf(e);
      var unitSources = e.sources.filter(function (s) { return units[s]; });
      var parent = e.parent && c4Elements[e.parent];
      var parts = below ? below.elements.filter(function (p) { var pe = c4Elements[p]; return pe && pe.parent === e.id; }).length : 0;
      append(side, [
        el("button", { type: "button", class: "ghost c4-close", "aria-label": "Close details", title: "Close (Esc)", on: { click: function () { select(null); } } }, "×"),
        el("h3", null, e.name),
        parent ? el("p", { class: "muted c4-in" }, "in ", c4ViewOf[parent.id] ? el("a", { href: c4Link(c4ViewOf[parent.id].id, e.id) }, parent.name) : parent.name) : null,
        el("p", { class: "muted" }, (C4_KIND[e.kind] || e.kind) + (e.technology ? " · " + e.technology : "")),
        e.description ? el("p", null, e.description) : null,
        e.tags.length ? el("p", null, e.tags.map(function (t) { return el("span", { class: "tag" }, t); })) : null,
        el("p", null,
          below ? el("a", { class: "c4-open-link", href: c4Link(below.id) }, "Open its " + C4_LEVEL[below.level].toLowerCase() + (parts ? " (" + parts + ")" : "") + " ›") : null,
          below && unitSources.length ? " · " : null,
          unitSources.length ? el("a", { href: "#/unit/" + enc(unitSources[0]) }, "Open the " + kindLabel(units[unitSources[0]].kind).toLowerCase()) : null,
          below && site ? " · " : null,
          site ? el("a", { href: site.href }, e.kind === "container" ? "Open in " + site.solution + "'s site ↗" : "Open " + e.name + "'s site ↗") : null),
        relList("Called by", v.relationships.filter(function (rel) { return rel.to === id; }), function (rel) { return rel.from; }),
        relList("Uses", v.relationships.filter(function (rel) { return rel.from === id; }), function (rel) { return rel.to; })
      ]);
    }

    canvas.svg.addEventListener("panend", function (ev) { if (!ev.detail.moved && selected) select(null); });

    // The level switcher: each level's nearest view; a level below that needs a choice says which.
    var levelButtons = C4_LEVELS.map(function (level, i) {
      return el("button", { type: "button", class: "c4-level", "data-level": level, "aria-pressed": String(level === v.level),
        title: "Level " + (i + 1) + ": " + C4_LEVEL[level] + " (key " + (i + 1) + ")",
        on: { click: function () { goLevel(level); } } },
        el("span", { class: "c4-level-n" }, String(i + 1)), " ", C4_LEVEL[level]);
    });
    function updateLevels() {
      levelButtons.forEach(function (b) {
        var level = b.getAttribute("data-level"), at = c4ViewAt(level, v, selected);
        b.disabled = !at.view;
        b.title = "Level " + (C4_LEVELS.indexOf(level) + 1) + ": " + C4_LEVEL[level] + (at.view ? (at.view.id === v.id ? " (this view)" : " (key " + (C4_LEVELS.indexOf(level) + 1) + ")") : ". " + at.reason);
      });
    }
    function goLevel(level) {
      var at = c4ViewAt(level, v, selected);
      if (at.view && at.view.id !== v.id) location.hash = c4Link(at.view.id, at.select);
    }
    var up = c4Up(v);
    var goUp = function () { if (up) location.hash = c4Link(up.view.id, up.select); };
    var sidebar = c4Sidebar(v);
    var navToggle = el("button", { type: "button", class: "c4-nav-toggle", "aria-pressed": String(c4NavShown), title: "Show or hide the list of views (key v)",
      on: { click: function () { toggleNav(); } } }, "☰ Views");
    function toggleNav(show) {
      c4NavShown = show === undefined ? !c4NavShown : show;
      try { localStorage.setItem(c4NavKey, c4NavShown ? "shown" : "hidden"); } catch (e) { /* private mode */ }
      sidebar.hidden = !c4NavShown;
      navToggle.setAttribute("aria-pressed", String(c4NavShown));
      if (c4NavShown) sidebar.reveal();
    }
    viewKeys = function (ev) {
      var n = "1234".indexOf(ev.key);
      if (n >= 0) { ev.preventDefault(); goLevel(C4_LEVELS[n]); }
      else if (ev.key === "u" || ev.key === "Backspace") { if (up) { ev.preventDefault(); goUp(); } }
      else if (ev.key === "v") { ev.preventDefault(); toggleNav(); }
      else if (ev.key === "/") { ev.preventDefault(); toggleNav(true); sidebar.filter.focus(); }
      else if (ev.key === "f") { ev.preventDefault(); find.focus(); }
      else if (ev.key === "0") { ev.preventDefault(); canvas.fit(); }
      else if (ev.key === "Escape" && selected) { select(null); }
      else if (ev.key === "Enter" && selected && c4ViewOf[selected] && !(ev.target && ev.target.closest && ev.target.closest("a, button, .c4-el"))) { location.hash = c4Link(c4ViewOf[selected].id); }
    };

    // Find in this view: matching elements stay lit while typing, Enter selects and centres the next match.
    var findAt = -1;
    var matches = function (q) {
      return g.nodes.filter(function (n) { var e = c4Elements[n.id]; return e && e.name.toLowerCase().indexOf(q) >= 0; })
        .sort(function (a, b) { return a.y - b.y || a.x - b.x; });
    };
    var find = el("input", { type: "search", class: "c4-find", placeholder: "Find in view…", "aria-label": "Find an element in this view",
      on: {
        input: function () {
          var q = find.value.trim().toLowerCase();
          findAt = -1;
          if (selected) select(null);
          var hits = q ? matches(q).map(function (n) { return n.id; }) : [];
          spotlight(q ? hits : null, []);
          hits.forEach(function (k) { if (nodeEls[k]) nodeEls[k].classList.add("found"); });
          find.classList.toggle("none", !!q && !hits.length);
        },
        keydown: function (ev) {
          if (ev.key === "Escape") { find.value = ""; find.dispatchEvent(new Event("input")); find.blur(); return; }
          if (ev.key !== "Enter") return;
          var q = find.value.trim().toLowerCase(), list = q ? matches(q) : [];
          if (!list.length) return;
          findAt = (findAt + (ev.shiftKey ? -1 : 1) + list.length) % list.length;
          var n = list[findAt];
          select(n.id);
          canvas.focus(n.x, n.y, n.width, n.height, 900, 560);
        }
      } });

    var trail = c4Trail(v);
    append(page, [
      el("div", { class: "toolbar" },
        navToggle,
        el("div", { class: "c4-levels", role: "group", "aria-label": "C4 level" }, levelButtons),
        up ? el("button", { type: "button", class: "c4-up", title: "Up to " + C4_LEVEL[up.view.level].toLowerCase() + " (key u)", on: { click: goUp } }, "↑ Up") : null,
        el("nav", { class: "c4-trail", "aria-label": "C4 levels" }, trail.map(function (t, i) {
          return [i ? el("span", { class: "muted" }, " › ") : null,
            t.view.id === v.id ? el("strong", { "aria-current": "page" }, t.label) : el("a", { href: c4Link(t.view.id) }, t.label)];
        })),
        el("span", { class: "spacer" }),
        (window.FLOORPLAN_FILES || []).indexOf("workspace.dsl") >= 0
          ? el("a", { class: "c4-dsl", href: "workspace.dsl", title: "The C4 model as a Structurizr DSL workspace; workspace.json beside it holds this layout" }, "Structurizr DSL")
          : null,
        find,
        quiet ? el("label", { class: "c4-labels" }, el("input", { type: "checkbox", on: { change: function (ev) { canvas.world.classList.toggle("c4-quiet", !ev.target.checked); } } }), " All labels") : null,
        zoomButtons(canvas),
        el("button", { type: "button", title: "Actual size: the diagram's text at its normal size", on: { click: canvas.actualSize } }, "1:1")),
      el("div", { class: "c4-body" }, sidebar, host)
    ]);
    host.appendChild(canvas.svg);
    host.appendChild(side);
    host.appendChild(el("div", { class: "legend c4-legend" },
      ["person", "software-system", "container", "component", "code", "external-system"].filter(function (k) {
        return g.nodes.some(function (n) { var e = c4Elements[n.id]; return e && e.kind === k; });
      }).map(function (k) {
        return el("span", null, el("i", { class: "c4-swatch c4-" + k }), C4_KIND[k]);
      }),
      g.nodes.some(function (n) { var e = c4Elements[n.id]; return e && !c4Inside(e, n.id) && (e.kind === "container" || e.kind === "component"); })
        ? el("span", null, el("i", { class: "c4-swatch c4-outside-swatch" }), "Outside this view's scope") : null,
      el("div", { class: "c4-hint" },
        "Level " + (C4_LEVELS.indexOf(v.level) + 1) + " of 4 · click an element for details, double-click to open the level below" +
        (quiet ? " · line labels show for the selected element" : "") + " · keys: 1–4 levels, u up, f find, v views, / filter views, 0 fit")));
    main.appendChild(page);
    sidebar.reveal();

    // An element named by the link (the one a view was left from going up) is selected and centred.
    var pick = r.query.select;
    if (pick && nodes[pick]) {
      select(pick);
      var pn = nodes[pick];
      canvas.focus(pn.x, pn.y, pn.width, pn.height, 900, 560);
      if (nodeEls[pick]) nodeEls[pick].focus({ preventScroll: true });
    } else {
      updateLevels();
      if (c4Viewport[v.id]) canvas.setView(c4Viewport[v.id]);
    }
    viewLeave = function () { c4Viewport[v.id] = canvas.getView(); };
  }

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
        // The project name reads from the left, the last column ends at the right edge, the rest are centred.
        el("thead", null, el("tr", null, ["Project", "Language", "Target", "Kind", unitWord.many, "Entry", "Exit"].map(function (h, i, all) {
          return el("th", { scope: "col", class: i === 0 ? null : i === all.length - 1 ? "right" : "center" }, h);
        }))),
        el("tbody", null, D.projects.map(function (p) {
          var us = unitsByProject[p.id] || [];
          var ids = {}; us.forEach(function (u) { ids[u.id] = true; });
          return el("tr", null,
            el("td", null, el("a", { href: "#/units?project=" + enc(p.id) }, p.name)),
            el("td", { class: "center" }, LANGUAGE_LABEL[p.language] || p.language, p.supportLevel === "limited" ? [" ", el("span", { class: "tag limited", title: "Shown as one node; effects attributed to the whole project" }, "limited support")] : null),
            el("td", { class: "center" }, p.targetFramework),
            el("td", { class: "center" }, p.isApplication ? "Application" : "Library"),
            el("td", { class: "num center" }, p.supportLevel === "limited" ? "—" : String(us.filter(function (u) { return u.kind !== "project"; }).length)),
            el("td", { class: "num center" }, String(D.entryPoints.filter(function (e) { return ids[e.handlerUnit]; }).length)),
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
    // Moving the view draws every visible text again, which is what makes a big diagram slow. So while the view
    // is being dragged or wheeled, the picture already drawn is moved and scaled as a whole (a CSS transform,
    // which the browser does without drawing), and the view is drawn anew (its viewBox) only when the movement
    // pauses or ends. Then text too small to read is left out (the svg gets "lod-far" or "lod-tiny"), and so is
    // what the view tracks (canvas.track) while it is well outside the screen.
    var rect = null;
    // The svg's box on the page: its parent's, as the svg itself may be transformed for the moment.
    function box() { if (!rect || !rect.width) rect = (root.parentNode || root).getBoundingClientRect(); return rect; }
    if (window.ResizeObserver) new ResizeObserver(function () { rect = null; apply(); }).observe(root);
    else window.addEventListener("resize", function () { rect = null; apply(); });
    var drawn = { x: view.x, y: view.y, w: view.w, h: view.h };
    var moving = false, settle = null, pending = false;
    // Movement: shown at once by transform, drawn after it rests for a moment.
    function move() {
      moving = true;
      clearTimeout(settle);
      // A drag that rests is drawn where it is too; moving on transforms that new picture.
      settle = setTimeout(function () { settle = null; moving = false; apply(); }, 160);
      apply();
    }
    function apply() { if (!pending) { pending = true; requestAnimationFrame(flush); } }
    function flush() {
      pending = false;
      if (moving) { transform(); return; }
      root.style.transform = "";
      drawn = { x: view.x, y: view.y, w: view.w, h: view.h };
      root.setAttribute("viewBox", view.x + " " + view.y + " " + view.w + " " + view.h);
      detail();
    }
    // The transform that turns the picture drawn for `drawn` into the picture for `view`.
    function transform() {
      var r = box();
      if (!r.width || !r.height) return;
      var place = function (v) { var s = Math.min(r.width / v.w, r.height / v.h); return { s: s, bx: -v.x * s + (r.width - v.w * s) / 2, by: -v.y * s + (r.height - v.h * s) / 2 }; };
      var a = place(drawn), b = place(view), k = b.s / a.s;
      root.style.transform = "translate(" + (b.bx - k * a.bx) + "px," + (b.by - k * a.by) + "px) scale(" + k + ")";
    }
    // The part of the diagram on screen, and screen pixels per unit of the diagram.
    function onScreen() {
      var r = box();
      if (!r.width || !r.height) return null;
      var s = Math.max(view.w / r.width, view.h / r.height), vw = r.width * s, vh = r.height * s;
      return { x: view.x - (vw - view.w) / 2, y: view.y - (vh - view.h) / 2, w: vw, h: vh, scale: 1 / s };
    }
    var tracked = [], lod = "", culled = null;
    function detail() {
      var on = onScreen();
      if (!on) return;
      var next = on.scale < 0.2 ? "lod-tiny" : on.scale < 0.45 ? "lod-far" : "";
      if (next !== lod) { if (lod) root.classList.remove(lod); if (next) root.classList.add(next); lod = next; }
      if (!tracked.length) return;
      // The area drawn reaches half a screen past each edge, so that a drag shows what it brings in; it is
      // worked out again when the screen leaves it, or after zooming in far enough that most of it is off screen.
      if (culled && on.x >= culled.x && on.y >= culled.y && on.x + on.w <= culled.x + culled.w && on.y + on.h <= culled.y + culled.h && on.w * 4 > culled.w) return;
      culled = { x: on.x - on.w / 2, y: on.y - on.h / 2, w: on.w * 2, h: on.h * 2 };
      var c = culled;
      for (var i = 0; i < tracked.length; i++) {
        var t = tracked[i];
        var out = t.x1 < c.x || t.x0 > c.x + c.w || t.y1 < c.y || t.y0 > c.y + c.h;
        if (out !== t.out) { t.out = out; t.el.style.display = out ? "none" : ""; }
      }
    }
    root.setAttribute("viewBox", view.x + " " + view.y + " " + view.w + " " + view.h);

    // Zoomed in at most to three screen pixels per unit (beyond that text is drawn slowly and nothing more shows),
    // and out to eight times the whole diagram.
    function zoom(factor, cx, cy) {
      var r = box();
      var nw = Math.min(Math.max(view.w * factor, 80, r.width ? r.width / 3 : 0), full.w * 8);
      var f = nw / view.w;
      view.x = cx - (cx - view.x) * f;
      view.y = cy - (cy - view.y) * f;
      view.w *= f; view.h *= f;
    }
    function toWorld(evt) {
      var r = box();
      var scale = Math.max(view.w / r.width, view.h / r.height);
      var ox = (r.width * scale - view.w) / 2, oy = (r.height * scale - view.h) / 2;
      return { x: view.x - ox + (evt.clientX - r.left) * scale, y: view.y - oy + (evt.clientY - r.top) * scale, scale: scale };
    }
    root.addEventListener("wheel", function (e) {
      e.preventDefault();
      // The svg may have moved on the page (the sidebar shown or hidden).
      if (!moving) rect = null;
      var p = toWorld(e);
      zoom(e.deltaY > 0 ? 1.15 : 1 / 1.15, p.x, p.y);
      move();
    }, { passive: false });

    var drag = null;
    root.addEventListener("pointerdown", function (e) {
      if (e.target.closest && e.target.closest(".g-node, .toggle, .g-card a")) return;
      rect = null;
      drag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, scale: toWorld(e).scale, moved: false };
      root.setPointerCapture(e.pointerId);
      root.classList.add("panning");
    });
    root.addEventListener("pointermove", function (e) {
      if (!drag) return;
      var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
      if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
      if (!drag.moved) return;
      view.x = drag.vx - dx * drag.scale;
      view.y = drag.vy - dy * drag.scale;
      move();
    });
    function endDrag() {
      if (!drag) return;
      var moved = drag.moved;
      drag = null;
      root.classList.remove("panning");
      if (moved) { clearTimeout(settle); settle = null; moving = false; apply(); }
      root.dispatchEvent(new CustomEvent("panend", { detail: { moved: moved } }));
    }
    root.addEventListener("pointerup", endDrag);
    root.addEventListener("pointercancel", endDrag);

    return {
      svg: root, world: world,
      // Not drawn while the box from (x0, y0) to (x1, y1) is well outside the screen.
      track: function (node, x0, y0, x1, y1) { tracked.push({ el: node, x0: x0, y0: y0, x1: x1, y1: y1, out: false }); culled = null; apply(); },
      zoomIn: function () { zoom(1 / 1.3, view.x + view.w / 2, view.y + view.h / 2); apply(); },
      zoomOut: function () { zoom(1.3, view.x + view.w / 2, view.y + view.h / 2); apply(); },
      fit: function () { view.x = full.x; view.y = full.y; view.w = full.w; view.h = full.h; apply(); },
      getView: function () { return { x: view.x, y: view.y, w: view.w, h: view.h }; },
      setView: function (v) { view.x = v.x; view.y = v.y; view.w = v.w; view.h = v.h; apply(); },
      // One unit of the diagram to one screen pixel, around the centre of the view.
      actualSize: function () {
        var r = box();
        if (!r.width) return;
        var cx = view.x + view.w / 2, cy = view.y + view.h / 2;
        view.w = r.width; view.h = r.height; view.x = cx - view.w / 2; view.y = cy - view.h / 2; apply();
      },
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
          svg("title", { text: (p.name || b.id) + " — " + (p.targetFramework || "") + ", " + plural(count, unitWord.one.toLowerCase(), unitWord.many.toLowerCase()) + (p.supportLevel === "limited" ? ", limited support" : "") }));
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
        el("h3", null, fullName(u)),
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
        var hit = D.units.filter(function (u) { return fullName(u).toLowerCase().indexOf(q) >= 0; })[0];
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

  // ---------- Flow graph ----------
  // Where requests enter and side-effects leave. The layout (D.layout.flowGraph) has three kinds of node, told
  // apart by id: "unit:<id>", "entries:<unit id>" (a card of the routes a unit handles) and
  // "exits:<unit id>|<category>" (a card of a unit's exit points of one category). Card geometry matches the layout.
  var CARD_HEADER = 26, CARD_LINE = 18, CARD_PADDING = 8;

  function flowGraph(r) {
    var g = D.layout.flowGraph;
    var page = el("div", { class: "graph-page" });
    if (!g || !g.nodes.length) {
      append(page, el("div", { class: "page" }, el("h1", null, "Flow"),
        el("p", { class: "empty" }, "No entry points or exit points were found, so there is no flow to show.")));
      main.appendChild(page);
      return;
    }
    var host = el("div", { class: "graph" });
    var canvas = graphCanvas(g.width, g.height);
    var side = el("div", { class: "side", hidden: true });
    var edgesLayer = svg("g"), nodesLayer = svg("g");
    canvas.world.appendChild(edgesLayer);
    canvas.world.appendChild(nodesLayer);
    var selected = null;
    var nodeEls = [], edgeEls = [];
    var nodeKey = {};
    g.nodes.forEach(function (n) { nodeKey[n.id] = n.id; n.members.forEach(function (m) { nodeKey[m] = n.id; }); });

    function kindOf(id) { return id.slice(0, id.indexOf(":")); }
    function unitOfCard(id) { var rest = id.slice(id.indexOf(":") + 1); var bar = rest.lastIndexOf("|"); return id.indexOf("exits:") === 0 && bar >= 0 ? rest.slice(0, bar) : rest; }
    function categoryOfCard(id) { return id.slice(id.lastIndexOf("|") + 1); }
    function capacity(n) { return Math.max(1, Math.round((n.height - CARD_HEADER - CARD_PADDING) / CARD_LINE)); }

    // Items of a card: [{ text, title, href }], one per distinct text, in a stable order.
    function entryItems(unitId) {
      var seen = {}, items = [];
      (entryByUnit[unitId] || []).slice().sort(function (a, b) { return routeLabel(a) < routeLabel(b) ? -1 : 1; }).forEach(function (e) {
        var t = routeLabel(e);
        if (seen[t]) { seen[t].count++; return; }
        seen[t] = { text: t, title: t + " — " + shortMember(e.handlerMember) + " (" + e.completeness + ")", href: "#/evidence/entry:" + enc(e.id), completeness: e.completeness, count: 1 };
        items.push(seen[t]);
      });
      return items;
    }
    function exitText(x) { return x.payload && x.payload.value !== null && x.payload.value !== undefined ? x.payload.value : shortMember(x.primitive); }
    function exitItems(unitId, category) {
      var seen = {}, items = [];
      (exitByUnit[unitId] || []).filter(function (x) { return x.category === category && visibleExit(x); }).forEach(function (x) {
        var t = exitText(x);
        if (seen[t]) { seen[t].count++; return; }
        seen[t] = { text: t, title: t + " — " + shortMember(x.member) + " via " + shortMember(x.primitive), href: "#/evidence/exit:" + enc(x.id), count: 1 };
        items.push(seen[t]);
      });
      return items;
    }

    function card(n, cls, header, items, moreHref) {
      var cap = capacity(n);
      var shown = items.length > cap ? items.slice(0, cap - 1) : items;
      var grp = svg("g", { class: "g-card " + cls },
        svg("rect", { x: n.x, y: n.y, width: n.width, height: n.height, rx: 6 }),
        svg("text", { class: "head", x: n.x + 10, y: n.y + 17, text: nodeText(header, n.width) }));
      shown.forEach(function (it, i) {
        var label = nodeText(it.text + (it.count > 1 ? " ×" + it.count : ""), n.width - 4);
        grp.appendChild(svg("a", { href: it.href, class: "line" + (it.completeness ? " " + it.completeness : "") },
          svg("text", { x: n.x + 10, y: n.y + CARD_HEADER + i * CARD_LINE + 13, text: label }),
          svg("title", { text: it.title })));
      });
      if (items.length > shown.length) {
        grp.appendChild(svg("a", { href: moreHref, class: "line more" },
          svg("text", { x: n.x + 10, y: n.y + CARD_HEADER + shown.length * CARD_LINE + 13, text: "+" + (items.length - shown.length) + " more…" })));
      }
      return grp;
    }

    function unitNode(n) {
      var ids = (n.members.length ? n.members : [n.id]).map(function (m) { return m.slice("unit:".length); });
      var u = units[ids[0]] || {};
      var label = ids.map(function (i) { return (units[i] || {}).displayName || i; }).join(" ⇄ ");
      var select = function () { selectNode(n.id); };
      var open = function () { location.hash = "#/unit/" + enc(ids[0]); };
      return svg("g", { class: "g-node flow-unit" + (n.members.length ? " group" : ""), tabindex: "0", role: "button",
        "aria-label": kindLabel(u.kind) + " " + ids.map(unitName).join(", ") + " in " + projectName(u.projectId),
        on: {
          click: select, dblclick: open,
          keydown: function (e) {
            if (e.key === "Enter") { e.preventDefault(); if (selected === n.id) open(); else select(); }
            else if (e.key === " ") { e.preventDefault(); select(); }
            else if (e.key === "Escape") { selectNode(null); }
          }
        } },
        svg("rect", { x: n.x, y: n.y, width: n.width, height: n.height, rx: 6 }),
        svg("text", { x: n.x + n.width / 2, y: n.y + 19, "text-anchor": "middle", text: nodeText(label, n.width) }),
        svg("text", { class: "project", x: n.x + n.width / 2, y: n.y + 35, "text-anchor": "middle", text: nodeText(projectName(u.projectId), n.width * 1.15) }),
        svg("title", { text: ids.map(unitName).join("\n") + "\n" + projectName(u.projectId) }));
    }

    g.edges.forEach(function (e) {
      var cls = "g-edge" + (kindOf(e.from) === "entries" ? " flow-in" : kindOf(e.to) === "exits" ? " flow-out" : "");
      var path = svg("path", { class: cls, d: curve(e.points) });
      path.__ends = [e.from, e.to];
      edgesLayer.appendChild(path);
      edgeEls.push(path);
    });

    g.nodes.forEach(function (n) {
      var kind = kindOf(n.id), node;
      if (kind === "entries") {
        var unitId = unitOfCard(n.id), items = entryItems(unitId);
        node = card(n, "in", "Entry points · " + (entryByUnit[unitId] || []).length, items, "#/unit/" + enc(unitId));
      } else if (kind === "exits") {
        var owner = unitOfCard(n.id), cat = categoryOfCard(n.id), xs = exitItems(owner, cat);
        var total = xs.reduce(function (a, it) { return a + it.count; }, 0);
        node = card(n, "out cat-" + cat, (CATEGORY_LABEL[cat] || cat) + " · " + total, xs, "#/unit/" + enc(owner));
      } else {
        node = unitNode(n);
      }
      node.__id = n.id;
      nodeEls.push(node);
      nodesLayer.appendChild(node);
    });

    function selectNode(id) {
      selected = id;
      var near = {};
      if (id) {
        near[id] = true;
        g.edges.forEach(function (e) { if (e.from === id) near[e.to] = true; if (e.to === id) near[e.from] = true; });
      }
      nodeEls.forEach(function (x) {
        x.classList.toggle("dim", !!id && !near[x.__id]);
        x.classList.toggle("selected", x.__id === id);
      });
      edgeEls.forEach(function (x) { x.classList.toggle("dim", !!id && x.__ends.indexOf(id) < 0); });
      renderSide();
    }

    function renderSide() {
      side.textContent = "";
      if (!selected) { side.hidden = true; return; }
      var ids = [selected];
      g.nodes.forEach(function (n) { if (n.id === selected && n.members.length) ids = n.members; });
      var unitId = ids[0].slice("unit:".length);
      var u = units[unitId];
      if (!u) { side.hidden = true; return; }
      side.hidden = false;
      var ins = entryByUnit[unitId] || [];
      var outs = (exitByUnit[unitId] || []).filter(visibleExit);
      var byCat = groupBy(outs, "category");
      append(side, [
        el("h3", null, fullName(u)),
        el("div", { class: "muted" }, kindLabel(u.kind), " in ", projectName(u.projectId)),
        el("p", null, el("a", { href: "#/unit/" + enc(u.id) }, "Open details"), " · ",
          el("a", { href: "#/units?unit=" + enc(u.id) + "&project=" + enc(u.projectId) }, "Show in " + unitWord.many.toLowerCase())),
        el("h4", null, "Entry points (" + ins.length + ")"),
        ins.length ? el("ul", { class: "plain" }, ins.slice(0, 20).map(function (e) {
          return el("li", null, el("a", { class: "mono", href: "#/evidence/entry:" + enc(e.id) }, routeLabel(e)), " ", el("span", { class: "tag " + e.completeness }, e.completeness));
        })) : el("div", { class: "muted" }, "None"),
        el("h4", null, "Exit points (" + outs.length + ")"),
        outs.length ? Object.keys(byCat).sort().map(function (c) {
          return el("div", null, el("div", null, el("span", { class: "tag" }, CATEGORY_LABEL[c] || c)),
            el("ul", { class: "plain" }, byCat[c].slice(0, 12).map(function (x) {
              return el("li", null, el("a", { class: "mono", href: "#/evidence/exit:" + enc(x.id) }, exitText(x)));
            })));
        }) : el("div", { class: "muted" }, "None")
      ]);
    }

    canvas.svg.addEventListener("panend", function (e) { if (!e.detail.moved) selectNode(null); });

    // Frames a node with its cards and the units it uses and is used by.
    function frame(id) {
      var ends = {}; ends[id] = true;
      g.edges.forEach(function (e) { if (e.from === id) ends[e.to] = true; if (e.to === id) ends[e.from] = true; });
      var near = g.nodes.filter(function (n) { return ends[n.id]; });
      if (!near.length) return;
      var x1 = Math.min.apply(null, near.map(function (n) { return n.x; })), y1 = Math.min.apply(null, near.map(function (n) { return n.y; }));
      var x2 = Math.max.apply(null, near.map(function (n) { return n.x + n.width; })), y2 = Math.max.apply(null, near.map(function (n) { return n.y + n.height; }));
      canvas.focus(x1, y1, x2 - x1, y2 - y1, 900, 560);
    }

    var search = el("input", { type: "search", placeholder: "Find " + unitWord.one.toLowerCase() + " or route…", "aria-label": "Find " + unitWord.one.toLowerCase() + " or route",
      on: { keydown: function (e) {
        if (e.key !== "Enter") return;
        var q = search.value.toLowerCase();
        if (!q) return;
        var hit = g.nodes.filter(function (n) {
          if (kindOf(n.id) !== "unit") return false;
          var unitId = n.id.slice("unit:".length), u = units[unitId] || {};
          return (u.name || u.displayName || "").toLowerCase().indexOf(q) >= 0 ||
            (entryByUnit[unitId] || []).some(function (x) { return routeLabel(x).toLowerCase().indexOf(q) >= 0; });
        })[0];
        if (!hit) return;
        selectNode(hit.id);
        frame(hit.id);
      } } });

    var handlers = g.nodes.filter(function (n) { return kindOf(n.id) === "entries"; }).length;
    var exitCards = g.nodes.filter(function (n) { return kindOf(n.id) === "exits"; }).length;
    append(page, [
      el("div", { class: "toolbar" },
        el("strong", null, "Flow"), search,
        el("span", { class: "muted" }, "Routes enter at the top, run through the " + unitWord.many.toLowerCase() + " that handle them and those they use, and leave as side-effects at the bottom. " +
          plural(handlers, "handler") + ", " + plural(exitCards, "kind of exit", "kinds of exit") + " by " + unitWord.one.toLowerCase() + "."),
        el("span", { class: "spacer" }), zoomButtons(canvas)),
      host
    ]);
    host.appendChild(canvas.svg);
    host.appendChild(side);
    host.appendChild(el("div", { class: "legend", "aria-hidden": "true" },
      el("span", null, el("i", { class: "in" }), "enters"), el("span", null, el("i"), "uses"), el("span", null, el("i", { class: "out" }), "side-effect")));
    main.appendChild(page);
    var focusUnit = r.query.unit && nodeKey["unit:" + r.query.unit];
    if (focusUnit) {
      selectNode(focusUnit);
      frame(focusUnit);
    }
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
      el("p", { class: "muted" }, el("a", { href: "#/units?unit=" + enc(u.id) + "&project=" + enc(u.projectId) }, "← Show in graph"),
        (D.layout.flowGraph && D.layout.flowGraph.nodes.some(function (n) { return n.id === "unit:" + u.id || n.members.indexOf("unit:" + u.id) >= 0; }))
          ? [" · ", el("a", { href: "#/flow?unit=" + enc(u.id) }, "Show in flow")] : null),
      el("h1", null, fullName(u)),
      el("p", { class: "sub" }, kindLabel(u.kind), " in ", el("a", { href: "#/units?project=" + enc(u.projectId) }, p.name || u.projectId),
        u.location ? [" · ", el("span", { class: "mono", title: u.location.file }, loc(u.location))] : null,
        u.parent ? [" · inside ", unitLink(u.parent)] : null),
      p.supportLevel === "limited" ? el("div", { class: "notice warning" }, "This project's language has limited support. It is shown as a single node and every effect found in it is attributed to the project as a whole.") : null,
      children.length ? [el("h2", null, "Contains"), el("ul", null, children.map(function (c) { return el("li", null, unitLink(c.id)); }))] : null,
      (u.types || []).length ? [el("h2", null, "Types (" + u.types.length + ")"), el("p", { class: "mono unit-types" }, u.types.join(", "))] : null,
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
    if (e.category === "background") return "(started without a request)";
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

  // ---------- Landscape: solutions and the interactions between them ----------
  // A table's columns: the first reads from the left, the last ends at the right edge, the rest are centred.
  function columnAlign(i, count) { return i === 0 ? null : i === count - 1 ? "right" : "center"; }
  function landscapeTable(heads, rows) {
    return el("div", { class: "table-wrap" }, el("table", null,
      el("thead", null, el("tr", null, heads.map(function (h, i) { return el("th", { scope: "col", class: columnAlign(i, heads.length) }, h); }))),
      el("tbody", null, rows.map(function (cells) {
        return el("tr", null, cells.map(function (c, i) { return el("td", { class: columnAlign(i, cells.length) }, c); }));
      }))));
  }
  function solutionHref(s, page) { return siteHref(s.slug, "#/" + (page || "overview")); }
  function systemName(id) { var e = c4Elements[id]; return e ? e.name : id; }
  function landscapeSource(id) { return (LANDSCAPE.sources || []).filter(function (s) { return s.id === id; })[0] || null; }
  function sourceHref(id) {
    var s = landscapeSource(id), slug = id.split(":")[0];
    var sol = LANDSCAPE.solutions.filter(function (x) { return x.slug === slug; })[0];
    return s && sol ? siteHref(sol.slug, s.page) : null;
  }

  function solutions() {
    var L = LANDSCAPE, page = el("div", { class: "page" });
    var out = {}, inn = {};
    L.interactions.forEach(function (i) { out[i.fromSystem] = (out[i.fromSystem] || 0) + 1; inn[i.toSystem] = (inn[i.toSystem] || 0) + 1; });
    var analyzed = L.solutions.filter(function (s) { return !s.failure; });
    var sum = function (k) { return analyzed.reduce(function (a, s) { return a + s[k]; }, 0); };
    function card(n, label, href) { return el(href ? "a" : "div", { class: "card", href: href }, el("div", { class: "n" }, String(n)), el("div", { class: "l" }, label)); }
    var failed = L.solutions.length - analyzed.length;

    append(page, [
      el("h1", null, L.name),
      el("p", { class: "sub" }, el("span", { class: "mono" }, L.path), " · ", plural(L.solutions.length, "solution"), " · generated ", L.generatedAt, " · Floorplan ", L.toolVersion),
      failed ? el("div", { class: "notice error" }, plural(failed, "solution"), " could not be analyzed. ", el("a", { href: "#/diagnostics" }, "See diagnostics.")) : null,
      el("div", { class: "cards" },
        card(L.solutions.length, "Solutions"),
        card(sum("applications"), "Applications"),
        card(sum("projects"), "Projects"),
        card(sum("entryPoints"), "Entry points"),
        card(sum("exitPoints"), "Exit points"),
        card(L.interactions.length, "Interactions between them", "#/interactions")),
      el("p", { class: "muted" }, "Each solution is a system. Open one to see its projects, code, architecture and evidence",
        D.architecture.views.length ? ["; ", el("a", { href: "#/architecture" }, "the Landscape"), " shows how the systems talk to each other."] : "."),
      el("h2", null, "Solutions"),
      landscapeTable(["Solution", "Path", "Language", "Target", "Applications", "Projects", "Entry", "Exit", "Calls out", "Called by", "Diagnostics"],
        L.solutions.map(function (s) {
          var id = "system:" + s.slug;
          var problems = (s.errors ? plural(s.errors, "error") : "") + (s.errors && s.warnings ? ", " : "") + (s.warnings ? plural(s.warnings, "warning") : "");
          if (s.failure) {
            return [el("span", null, s.name, " ", el("span", { class: "tag limited" }, "not analyzed")), el("span", { class: "mono" }, s.path),
              "", "", "", "", "", "", "", "", el("a", { href: "#/diagnostics" }, problems)];
          }
          return [
            el("a", { href: solutionHref(s) }, s.name),
            el("span", { class: "mono" }, s.path),
            s.languages.map(function (l) { return LANGUAGE_LABEL[l] || l; }).join(", "),
            s.frameworks.join(", "),
            String(s.applications), String(s.projects), String(s.entryPoints), String(s.exitPoints),
            out[id] ? el("a", { href: "#/interactions?from=" + enc(id) }, String(out[id])) : "0",
            inn[id] ? el("a", { href: "#/interactions?to=" + enc(id) }, String(inn[id])) : "0",
            problems ? el("a", { href: solutionHref(s, "diagnostics") }, problems) : "none"
          ];
        })),
      L.nested.length ? [
        el("h2", null, "Not analyzed"),
        el("p", { class: "muted" }, "Solutions in the folder of another solution are part of its system, so they are not analyzed unless named with ",
          el("span", { class: "mono" }, "--solutions"), ":"),
        el("ul", null, L.nested.map(function (n) { return el("li", { class: "mono" }, n); }))
      ] : null
    ]);
    main.appendChild(page);
  }

  var MATCHED_BY = {
    "queue": ["queue name", "A message to a queue, topic or address the other system receives at, through the same library"],
    "landscape-file": ["landscape file", "floorplan.landscape.json maps the URL's host to the other system"],
    "host": ["host names it", "The URL's host names the other system or one of its applications"],
    "route-only": ["route only", "No host names a system, but only this system has the route: probable, since two deployments can run the same route"]
  };

  function interactions(r) {
    var L = LANDSCAPE, page = el("div", { class: "page" });
    var list = L.interactions.filter(function (i) { return (!r.query.from || i.fromSystem === r.query.from) && (!r.query.to || i.toSystem === r.query.to); });
    var filter = r.query.from ? " from " + systemName(r.query.from) : r.query.to ? " to " + systemName(r.query.to) : "";
    function evidenceLinks(i) {
      return el("span", null, i.sources.map(function (s, k) {
        var href = sourceHref(s);
        return [k ? el("br") : null, href ? el("a", { href: href }, k ? "entry point ↗" : "exit point ↗") : s];
      }));
    }
    append(page, [
      el("h1", null, "Interactions" + filter),
      el("p", { class: "sub" }, "Every call one system makes that another one receives: the exit point in the caller's code and the entry point in the receiver's. ",
        filter ? el("a", { href: "#/interactions" }, "Show all.") : null),
      list.length ? landscapeTable(["From", "To", "What", "How", "Matched by", "Evidence"], list.map(function (i) {
        var by = MATCHED_BY[i.matchedBy] || [i.matchedBy, ""];
        return [
          [el("strong", null, systemName(i.fromSystem)), " › ", systemName(i.fromContainer), el("div", { class: "mono small muted" }, shortMember(i.fromMember))],
          [el("strong", null, systemName(i.toSystem)), " › ", systemName(i.toContainer), el("div", { class: "mono small muted" }, shortMember(i.toMember))],
          [el("span", { class: "mono" }, i.what), i.methods.length ? el("div", { class: "small muted" }, i.methods.join(", ")) : null,
            el("div", { class: "mono small muted", title: "As the caller's code has it" }, "called as " + i.called)],
          i.technology || "",
          el("span", { class: "tag" + (i.probable ? " limited" : ""), title: by[1] }, by[0] + (i.probable ? " (probable)" : "")),
          evidenceLinks(i)
        ];
      })) : el("p", { class: "empty" }, filter ? "None." : "No call of one system was found to reach another. Calls whose address is read from configuration are matched once the configuration is read."),
      el("h2", null, "How calls are matched"),
      el("ol", null, [
        "Messages: a queue, topic or address another system receives at, through the same library (also gRPC services, WCF actions and Orleans grains).",
        "HTTP to a host that floorplan.landscape.json in the analyzed folder maps to a system, to one of its routes.",
        "HTTP whose URL's host names another system or one of its applications, to one of its routes.",
        "HTTP to a route only one other system has, when the caller's own system does not have it: probable, drawn dashed.",
        "A folder (an absolute path) that more than one system reads, writes or watches is one external element they share, on the Landscape."
      ].map(function (t) { return el("li", null, t); }))
    ]);
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
