// ITS ResFix Staff — small UI behaviours (no framework).
(function () {
    "use strict";

    // ---- Live 48h countdowns: any element with data-deadline="<ISO UTC>" ----
    function format(ms, long) {
        var totalMinutes = Math.floor(ms / 60000);
        var hours = Math.floor(totalMinutes / 60);
        var minutes = totalMinutes % 60;
        if (long) return hours + "h " + String(minutes).padStart(2, "0") + "m left";
        return hours >= 1 ? hours + "h left" : Math.max(1, minutes) + "m left";
    }

    function updateCountdowns() {
        document.querySelectorAll("[data-deadline]").forEach(function (el) {
            var iso = el.getAttribute("data-deadline");
            if (!iso) return;
            var left = new Date(iso).getTime() - Date.now();
            var text = el.querySelector(".timer-text") || el;
            el.classList.remove("timer-ok", "timer-warning", "timer-expired");
            if (left <= 0) {
                text.textContent = "EXPIRED";
                el.classList.add("timer-expired");
            } else {
                text.textContent = format(left, el.getAttribute("data-format") === "long");
                el.classList.add(left < 12 * 3600000 ? "timer-warning" : "timer-ok");
            }
        });
    }

    updateCountdowns();
    setInterval(updateCountdowns, 30000);

    // ---- Job-detail timer box (data-countdown-box) ----
    function updateTimerBox(box) {
        var iso = box.getAttribute("data-deadline");
        if (!iso) return; // resolved — static display
        var assignedIso = box.getAttribute("data-assigned");
        var deadline = new Date(iso).getTime();
        var now = Date.now();
        var left = deadline - now;

        var remainEl   = box.querySelector("[data-remaining]");
        var progressEl = box.querySelector("[data-progress]");
        var percentEl  = box.querySelector("[data-percent]");
        var stateEl    = box.querySelector("[data-state]");

        if (left <= 0) {
            if (remainEl)   remainEl.textContent = "0 minutes";
            if (progressEl) progressEl.style.width = "0%";
            if (percentEl)  percentEl.textContent = "0";
            if (stateEl)    stateEl.textContent = "EXPIRED";
            box.classList.remove("timer-box-active");
            box.classList.add("timer-box-expired");
            return;
        }

        // Remaining text
        var hours = Math.floor(left / 3600000);
        var minutes = Math.floor((left % 3600000) / 60000);
        var remaining = hours >= 1 ? hours + " hour" + (hours === 1 ? "" : "s") : Math.max(1, minutes) + " minutes";
        if (remainEl) remainEl.textContent = remaining;

        // Percent
        var start = assignedIso ? new Date(assignedIso).getTime() : deadline - 48 * 3600000;
        var window = deadline - start;
        var pct = window > 0 ? Math.round(100 * (deadline - now) / window) : 0;
        pct = Math.max(0, Math.min(100, pct));
        if (progressEl) progressEl.style.width = pct + "%";
        if (percentEl)  percentEl.textContent = pct;

        if (stateEl) stateEl.textContent = "ACTIVE";
    }

    function updateAllTimerBoxes() {
        document.querySelectorAll("[data-countdown-box]").forEach(updateTimerBox);
    }

    updateAllTimerBoxes();
    setInterval(updateAllTimerBoxes, 60000);

    // ---- One designed confirm dialog for every "are you sure?" (replaces the browser's plain popup) ----
    var confirmEl = null;
    function askConfirm(message, onYes) {
        if (!window.bootstrap) { if (window.confirm(message)) onYes(); return; }
        if (!confirmEl) {
            confirmEl = document.createElement("div");
            confirmEl.className = "modal fade pm-modal pm-confirm";
            confirmEl.tabIndex = -1;
            confirmEl.setAttribute("aria-hidden", "true");
            confirmEl.innerHTML =
                '<div class="modal-dialog modal-dialog-centered"><div class="modal-content" role="alertdialog" aria-labelledby="rfConfirmTitle">' +
                '<div class="modal-body"><span class="pm-confirm-icon"><i class="bi"></i></span>' +
                '<h2 class="pm-confirm-title" id="rfConfirmTitle">Are you sure?</h2><p class="pm-confirm-text"></p></div>' +
                '<div class="modal-footer pm-modal-foot"><button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>' +
                '<button type="button" class="btn" data-confirm-yes>Confirm</button></div></div></div>';
            document.body.appendChild(confirmEl);
        }
        var danger = /^\s*(delete|remove|decline)/i.test(message);
        confirmEl.querySelector(".pm-confirm-text").textContent = message;
        confirmEl.querySelector(".pm-confirm-icon").className = "pm-confirm-icon" + (danger ? " pm-confirm-danger" : "");
        confirmEl.querySelector(".pm-confirm-icon i").className = "bi " + (danger ? "bi-trash" : "bi-question-lg");
        var yes = confirmEl.querySelector("[data-confirm-yes]");
        yes.className = "btn " + (danger ? "btn-danger" : "btn-rf-primary");
        yes.textContent = danger ? "Yes, " + message.trim().split(/\s+/)[0].toLowerCase() : "Yes, continue";
        var modal = window.bootstrap.Modal.getOrCreateInstance(confirmEl);
        var done = false;
        yes.onclick = function () { done = true; modal.hide(); };
        confirmEl.addEventListener("hidden.bs.modal", function once() {
            confirmEl.removeEventListener("hidden.bs.modal", once);
            if (done) onYes();
        });
        modal.show();
    }

    // ---- Behaviours that used to be inline handlers (the page policy blocks inline scripts) ----
    document.addEventListener("click", function (e) {
        var tab = e.target.closest(".login-tab[data-placeholder]");
        if (tab) {
            document.querySelectorAll(".login-tab").forEach(function (t) { t.classList.remove("active"); });
            tab.classList.add("active");
            var email = document.getElementById("emailInput");
            if (email) email.placeholder = tab.getAttribute("data-placeholder");
            return;
        }
        var btn = e.target.closest("button[data-confirm], input[data-confirm]");
        if (btn) {
            if (btn.dataset.confirmed) { delete btn.dataset.confirmed; }
            else {
                e.preventDefault();
                askConfirm(btn.getAttribute("data-confirm"), function () {
                    btn.dataset.confirmed = "1";
                    if (btn.form) btn.form.requestSubmit(btn); else btn.click();
                });
                return;
            }
        }
        var edit = e.target.closest("[data-student-edit]");
        if (edit && window.bootstrap) {
            var get = function (n) { return edit.getAttribute(n) || ""; };
            document.getElementById("editStudentId").value = get("data-student-edit");
            document.getElementById("editStudentName").textContent = "Fix a mistake in the details you entered.";
            document.getElementById("editStudentFullName").value = get("data-student-name");
            document.getElementById("editStudentRoom").value = get("data-student-room");
            document.getElementById("editStudentPhone").value = get("data-student-phone");
            var emailBox = document.getElementById("editStudentEmail");
            emailBox.value = get("data-student-email");
            emailBox.dataset.original = emailBox.value.toLowerCase();
            document.getElementById("editStudentPhone").dataset.original = get("data-student-phone");
            var registered = get("data-student-registered") === "1";
            document.getElementById("editStudentCancelRow").classList.toggle("d-none", !registered);
            document.getElementById("editStudentCancel").checked = false;
            window.bootstrap.Modal.getOrCreateInstance(document.getElementById("editStudentModal")).show();
            return;
        }
        var acc = e.target.closest("[data-access]");
        if (acc && window.bootstrap) {
            var st = acc.getAttribute("data-status") || "active", kind = acc.getAttribute("data-kind");
            var nm = acc.getAttribute("data-name") || "";
            document.getElementById("accessKind").value = kind;
            document.getElementById("accessId").value = acc.getAttribute("data-id");
            document.getElementById("accessTitle").textContent = nm;
            document.getElementById("accessSub").textContent = "Currently " + st + ". Choose what happens next.";
            document.getElementById("accessJobCount").textContent = acc.getAttribute("data-jobs") || "0";
            document.getElementById("accessConfirmName").textContent = nm;
            document.getElementById("accessReason").value = "";
            document.getElementById("accessConfirm").value = "";
            document.getElementById("accessJobs").checked = true;
            var note = document.querySelector("[data-restore-note]");
            if (note) note.textContent = st === "removed"
                ? (kind === "worker" ? "They can sign in again after choosing a new password (a new welcome is sent). They start off duty." : "They can use the app again after setting up with First Login.")
                : "They can sign in again and carry on as before.";
            var first = null;
            document.querySelectorAll("#accessModal .access-option").forEach(function (o) {
                var show = (o.getAttribute("data-when") || "").split(" ").indexOf(st) >= 0;
                o.classList.toggle("d-none", !show);
                var r = o.querySelector("input"); r.checked = false;
                if (show && !first && r.value !== "delete") first = r;
            });
            document.getElementById("accessGo").disabled = true;
            window.bootstrap.Modal.getOrCreateInstance(document.getElementById("accessModal")).show();
            return;
        }
        var wedit = e.target.closest("[data-worker-edit]");
        if (wedit && window.bootstrap) {
            var wget = function (n) { return wedit.getAttribute(n) || ""; };
            document.getElementById("editWorkerId").value = wget("data-worker-edit");
            document.getElementById("editWorkerName").value = wget("data-name");
            var wEmail = document.getElementById("editWorkerEmail");
            wEmail.value = wget("data-email");
            wEmail.dataset.original = wEmail.value.toLowerCase();
            document.getElementById("editWorkerPhone").value = wget("data-phone");
            document.getElementById("editWorkerSpec").value = wget("data-spec");
            document.getElementById("editWorkerEmailNote").classList.add("d-none");
            window.bootstrap.Modal.getOrCreateInstance(document.getElementById("editWorkerModal")).show();
            return;
        }
        if (e.target.closest("[data-print]")) { window.print(); return; }
        var un = e.target.closest("[data-uncheck]");
        if (un) { var t = document.querySelector(un.getAttribute("data-uncheck")); if (t) t.checked = false; }
    });
    document.addEventListener("submit", function (e) {
        var form = e.target;
        var msg = form.getAttribute && form.getAttribute("data-confirm");
        if (!msg) return;
        if (form.dataset.confirmed) { delete form.dataset.confirmed; return; }
        e.preventDefault();
        askConfirm(msg, function () {
            form.dataset.confirmed = "1";
            if (e.submitter) form.requestSubmit(e.submitter); else form.requestSubmit();
        });
    });
    document.addEventListener("change", function (e) {
        if (e.target.matches && e.target.matches("[data-autosubmit]") && e.target.form) e.target.form.submit();
    });

    // ---- Dialogs: reopen one after a failed save, and focus its first field ----
    if (window.bootstrap) {
        document.querySelectorAll(".modal[data-auto-open]").forEach(function (el) {
            window.bootstrap.Modal.getOrCreateInstance(el).show();
        });
        document.addEventListener("shown.bs.modal", function (e) {
            var f = e.target.querySelector("input:not([type=hidden]):not([disabled]), select, textarea");
            if (f) f.focus();
        });
    }

    // ---- Mobile sidebar ----
    document.querySelectorAll("[data-sidebar-toggle]").forEach(function (btn) {
        btn.addEventListener("click", function () { document.body.classList.toggle("sidebar-open"); });
    });
    document.querySelectorAll("[data-sidebar-close]").forEach(function (el) {
        el.addEventListener("click", function () { document.body.classList.remove("sidebar-open"); });
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") document.body.classList.remove("sidebar-open");
    });

    // ---- Live conversation: new messages, typing and seen ticks without reloading ----
    (function () {
        var box = document.querySelector("[data-chat]");
        if (!box || !window.fetch) return;
        var list = box.querySelector("[data-chat-list]"), empty = box.querySelector("[data-chat-empty]");
        var typingEl = box.querySelector("[data-chat-typing]"), typingName = box.querySelector("[data-chat-typing-name]");
        var form = box.querySelector("[data-chat-form]"), area = form && form.querySelector("textarea");
        var errorEl = form && form.querySelector("[data-chat-error]");
        var url = box.getAttribute("data-chat-url"), typingUrl = box.getAttribute("data-typing-url");
        var lastSig = null, lastCount = -1, lastPing = 0, failures = 0, timer = null;

        function node(tag, cls, text) {
            var n = document.createElement(tag);
            if (cls) n.className = cls;
            if (text != null) n.textContent = text;
            return n;
        }
        function nearBottom() { return list.scrollHeight - list.scrollTop - list.clientHeight < 60; }

        function render(snap) {
            var sig = snap.messages.map(function (m) { return m.id + (m.seen ? "s" : ""); }).join(",");
            if (sig !== lastSig) {
                var stick = lastSig === null || nearBottom() || snap.messages.length > lastCount;
                list.textContent = "";
                snap.messages.forEach(function (m) {
                    var staff = m.sender === "staff";
                    var row = node("div", "chat-row" + (staff ? " chat-row-staff" : ""));
                    var bub = node("div", "chat-bubble " + (staff ? "chat-bubble-staff" : "chat-bubble-student"));
                    bub.appendChild(node("div", "chat-name", (m.name || (staff ? "Staff" : "Student")) + " " + String.fromCharCode(183) + " " + m.time));
                    bub.appendChild(node("div", "chat-text", m.body));
                    if (m.photoUrl && /^https:\/\//.test(m.photoUrl)) {
                        var a = node("a"); a.href = m.photoUrl; a.target = "_blank"; a.rel = "noopener";
                        var img = node("img", "chat-photo"); img.src = m.photoUrl; img.alt = "Photo from the student";
                        a.appendChild(img); bub.appendChild(a);
                    }
                    if (staff) {
                        var t = node("div", "chat-ticks" + (m.seen ? " chat-ticks-seen" : ""));
                        t.title = m.seen ? "Seen by the student" : "Sent";
                        t.appendChild(node("i", "bi " + (m.seen ? "bi-check2-all" : "bi-check2")));
                        bub.appendChild(t);
                    }
                    row.appendChild(bub); list.appendChild(row);
                });
                if (empty) empty.classList.toggle("d-none", snap.messages.length > 0);
                if (stick) list.scrollTop = list.scrollHeight;
                lastSig = sig; lastCount = snap.messages.length;
            }
            var typing = snap.typingName;
            typingEl.classList.toggle("d-none", !typing);
            if (typing) { typingName.textContent = typing; if (nearBottom()) list.scrollTop = list.scrollHeight; }
        }

        function poll() {
            clearTimeout(timer);
            if (document.hidden) { timer = setTimeout(poll, 4000); return Promise.resolve(); }
            return fetch(url + "?seen=true", { credentials: "same-origin", cache: "no-store", headers: { "Accept": "application/json" } })
                .then(function (r) { if (!r.ok) throw new Error("status " + r.status); return r.json(); })
                .then(function (snap) { failures = 0; render(snap); })
                .catch(function () { failures++; })
                .then(function () { if (failures < 5) timer = setTimeout(poll, failures ? 8000 : 3000); });
        }

        if (form && area) {
            var token = form.querySelector("input[name=__RequestVerificationToken]");
            area.addEventListener("input", function () {
                var now = Date.now();
                if (!area.value.trim() || now - lastPing < 3000 || !token) return;
                lastPing = now;
                var fd = new FormData(); fd.append("__RequestVerificationToken", token.value);
                fetch(typingUrl, { method: "POST", body: fd, credentials: "same-origin" }).catch(function () {});
            });
            area.addEventListener("keydown", function (e) {
                if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); if (area.value.trim()) form.requestSubmit(); }
            });
            form.addEventListener("submit", function (e) {
                e.preventDefault();
                var btn = form.querySelector("button[type=submit]");
                btn.disabled = true; errorEl.classList.add("d-none");
                fetch(form.action, { method: "POST", body: new FormData(form), credentials: "same-origin", headers: { "X-Requested-With": "fetch" } })
                    .then(function (r) { return r.json(); })
                    .then(function (res) {
                        if (res.ok) { area.value = ""; lastPing = 0; return poll(); }
                        errorEl.textContent = res.error || "The message could not be sent."; errorEl.classList.remove("d-none");
                    })
                    .catch(function () { errorEl.textContent = "The message could not be sent. Check your connection and try again."; errorEl.classList.remove("d-none"); })
                    .then(function () { btn.disabled = false; area.focus(); });
            });
        }

        list.scrollTop = list.scrollHeight;
        timer = setTimeout(poll, 1500);
    })();

    // ---- Every staff page keeps itself up to date: fetch the page again and swap in what changed ----
    (function () {
        var content = document.querySelector(".staff-body .content");
        if (!content || !window.fetch || !window.DOMParser || !content.replaceChildren) return;
        var EVERY = 12000, running = false, failures = 0, timer = null;

        // true while the person is in the middle of something we must not disturb
        function busy() {
            if (document.hidden || document.body.classList.contains("modal-open")) return true;
            var a = document.activeElement;
            if (a && content.contains(a) && /^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName) && !a.closest("[data-chat]")) return true;
            var sel = window.getSelection && window.getSelection();
            if (sel && String(sel).length > 0) return true;
            var fields = content.querySelectorAll("input, textarea, select");
            for (var i = 0; i < fields.length; i++) {
                var f = fields[i];
                if (f.closest("[data-chat]") || f.type === "hidden") continue;
                if (f.tagName === "SELECT") { if (Array.prototype.some.call(f.options, function (o) { return o.selected !== o.defaultSelected; })) return true; }
                else if (f.type === "checkbox" || f.type === "radio") { if (f.checked !== f.defaultChecked) return true; }
                else if (f.type === "file") { if (f.value) return true; }
                else if (f.value !== f.defaultValue) return true;
            }
            return false;
        }

        // what the page looks like apart from things that change by themselves (form tokens, ticking timers, dialogs, chat, banners)
        function signature(root) {
            var c = root.cloneNode(true);
            c.querySelectorAll("[data-chat], .alert, .modal").forEach(function (n) { n.remove(); });
            c.querySelectorAll("input[name=__RequestVerificationToken]").forEach(function (n) { n.removeAttribute("value"); });
            c.querySelectorAll("[data-deadline], [data-countdown-box]").forEach(function (n) {
                n.textContent = ""; n.removeAttribute("class"); n.removeAttribute("style");
            });
            return c.innerHTML;
        }

        function swap(fresh) {
            var oldChat = content.querySelector("[data-chat]"), newChat = fresh.querySelector("[data-chat]");
            var keepChat = oldChat && newChat;
            if (keepChat) { var slot = document.createElement("div"); slot.setAttribute("data-chat-slot", ""); newChat.replaceWith(slot); }
            var oldAlerts = Array.prototype.slice.call(content.querySelectorAll(":scope > .alert"));
            var alertAt = oldAlerts.length ? Array.prototype.indexOf.call(content.children, oldAlerts[0]) : -1;
            var nodes = Array.prototype.map.call(fresh.childNodes, function (n) { return document.importNode(n, true); });
            if (alertAt >= 0) nodes.splice(Math.min(alertAt, nodes.length), 0, oldAlerts[0], ...oldAlerts.slice(1));
            var x = window.scrollX, y = window.scrollY;
            content.replaceChildren.apply(content, nodes);
            if (keepChat) { var s = content.querySelector("[data-chat-slot]"); if (s) s.replaceWith(oldChat); }
            window.scrollTo(x, y);
            updateCountdowns(); updateAllTimerBoxes();
        }

        function refreshNav(doc) {
            ["#sidebar .sidebar-nav", ".bottom-nav"].forEach(function (q) {
                var cur = document.querySelector(q), nxt = doc.querySelector(q);
                if (cur && nxt && cur.innerHTML.replace(/\s+/g, "") !== nxt.innerHTML.replace(/\s+/g, "")) cur.innerHTML = nxt.innerHTML;
            });
        }

        function tick() {
            clearTimeout(timer);
            if (running || busy()) { timer = setTimeout(tick, 4000); return; }
            running = true;
            fetch(location.href, { credentials: "same-origin", cache: "no-store", headers: { "X-Requested-With": "autorefresh", "Accept": "text/html" } })
                .then(function (r) {
                    var type = r.headers.get("content-type") || "";
                    if (!r.ok || r.redirected || type.indexOf("text/html") < 0) throw new Error("stop");
                    return r.text();
                })
                .then(function (html) {
                    var doc = new DOMParser().parseFromString(html, "text/html");
                    var fresh = doc.querySelector(".staff-body .content");
                    if (!fresh) throw new Error("stop");
                    failures = 0;
                    if (!busy() && signature(fresh) !== signature(content)) swap(fresh);
                    refreshNav(doc);
                })
                .catch(function () { failures++; })
                .then(function () { running = false; if (failures < 3) timer = setTimeout(tick, failures ? EVERY * 3 : EVERY); });
        }

        document.addEventListener("visibilitychange", function () { if (!document.hidden) { clearTimeout(timer); timer = setTimeout(tick, 800); } });
        timer = setTimeout(tick, EVERY);
    })();

    // ---- A photo that no longer exists shows a short note instead of a broken image ----
    document.addEventListener("error", function (e) {
        var img = e.target;
        if (!img || img.tagName !== "IMG" || !(img.classList.contains("complaint-photo") || img.classList.contains("chat-photo"))) return;
        var note = document.createElement("span");
        note.className = "text-muted small";
        note.textContent = "This photo is no longer available.";
        var link = img.closest("a");
        (link || img).replaceWith(note);
    }, true);

    // ---- Manage access dialog: show only the fields the chosen level needs ----
    document.addEventListener("change", function (e) {
        if (!e.target.matches || !e.target.matches("#accessModal input[name=action]")) return;
        var v = e.target.value, isWorker = document.getElementById("accessKind").value === "worker";
        document.querySelector("[data-access-block=reason]").classList.toggle("d-none", !(v === "suspend" || v === "remove"));
        document.querySelector("[data-access-block=jobs]").classList.toggle("d-none", !(isWorker && (v === "suspend" || v === "remove")));
        document.querySelector("[data-access-block=confirm]").classList.toggle("d-none", v !== "delete");
        document.getElementById("accessGo").disabled = false;
        document.getElementById("accessGo").className = "btn " + (v === "delete" || v === "remove" ? "btn-danger" : "btn-rf-primary");
    });

    // ---- Edit dialogs: explain what a changed email / number will cancel ----
    document.addEventListener("input", function (e) {
        var t = e.target;
        if (t.id === "editWorkerEmail") {
            document.getElementById("editWorkerEmailNote").classList.toggle("d-none", t.value.trim().toLowerCase() === (t.dataset.original || ""));
        } else if (t.id === "editStudentEmail" || t.id === "editStudentPhone") {
            var em = document.getElementById("editStudentEmail"), ph = document.getElementById("editStudentPhone");
            var changed = em.value.trim().toLowerCase() !== (em.dataset.original || "") || ph.value.trim() !== (ph.dataset.original || "");
            var row = document.getElementById("editStudentCancelRow");
            if (!row.classList.contains("d-none")) document.getElementById("editStudentCancel").checked = changed;
        }
    });

    // ---- Show / hide password ----
    document.querySelectorAll("[data-toggle-password]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var input = document.querySelector(btn.getAttribute("data-toggle-password"));
            if (!input) return;
            var show = input.type === "password";
            input.type = show ? "text" : "password";
            btn.querySelector("i").className = show ? "bi bi-eye-slash" : "bi bi-eye";
        });
    });
})();
