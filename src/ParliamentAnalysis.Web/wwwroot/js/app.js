const $ = (sel) => document.querySelector(sel);

async function api(path, body) {
  const response = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(body),
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.detail || `Request failed (${response.status})`);
  }
  return payload;
}

function setFeedback(message, isError = false) {
  const el = $("#analysisFeedback");
  el.hidden = !message;
  el.textContent = message || "";
  el.classList.toggle("error", Boolean(isError));
}

function renderDistribution(data) {
  $("#results").hidden = false;
  $("#metricTotal").textContent = data.total_persons ?? "—";
  $("#metricClassified").textContent = data.classified_persons ?? "—";
  $("#metricPeriod").textContent = data.wahlperiode ?? "—";

  const groups = data.groups || [];
  const max = Math.max(...groups.map((g) => g.percentage || 0), 1);
  const chart = $("#chart");
  chart.innerHTML = "";
  groups.forEach((group, index) => {
    const row = document.createElement("div");
    row.className = "bar-row";
    row.innerHTML = `
      <span class="bar-label">${escapeHtml(group.fraktion)}</span>
      <div class="bar-track"><div class="bar-fill" style="transition-delay:${index * 40}ms"></div></div>
      <span class="bar-value">${Number(group.percentage).toFixed(2)}%</span>
    `;
    chart.appendChild(row);
    requestAnimationFrame(() => {
      row.querySelector(".bar-fill").style.width = `${(group.percentage / max) * 100}%`;
    });
  });

  const body = $("#distributionBody");
  body.innerHTML = groups
    .map(
      (group) => `
      <tr>
        <td>${escapeHtml(group.fraktion)}</td>
        <td>${group.count}</td>
        <td>${Number(group.percentage).toFixed(2)}%</td>
      </tr>`
    )
    .join("");

  const notes = data.notes || [];
  const notesBox = $("#notesBox");
  if (notes.length) {
    notesBox.hidden = false;
    $("#notesList").innerHTML = notes.map((n) => `<li>${escapeHtml(n)}</li>`).join("");
  } else {
    notesBox.hidden = true;
  }
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function appendBubble(role, text) {
  const log = $("#chatLog");
  const bubble = document.createElement("div");
  bubble.className = `bubble ${role}`;
  bubble.textContent = text;
  log.appendChild(bubble);
  log.scrollTop = log.scrollHeight;
}

function periodValue() {
  return Number($("#wahlperiode").value || 20);
}

async function loadHealth() {
  try {
    const res = await fetch("/api/health");
    const data = await res.json();
    const bits = [
      data.status === "ok" ? "API bereit" : "API gestört",
      data.dip_configured ? "DIP ok" : "DIP-Key fehlt",
      data.groq_configured ? "Groq ok" : "Groq optional",
    ];
    $("#healthStatus").textContent = bits.join(" · ");
  } catch {
    $("#healthStatus").textContent = "API nicht erreichbar";
  }
}

$("#distributionForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = $("#loadDistribution");
  button.disabled = true;
  setFeedback("Lade Personen aus DIP… (kann einige Sekunden dauern)");
  try {
    const data = await api("/api/distribution", {
      wahlperiode: periodValue(),
      include_unknown: $("#includeUnknown").checked,
    });
    renderDistribution(data);
    $("#summaryBox").hidden = true;
    setFeedback("Verteilung geladen.");
  } catch (err) {
    setFeedback(err.message, true);
  } finally {
    button.disabled = false;
  }
});

$("#loadSummary").addEventListener("click", async () => {
  const button = $("#loadSummary");
  button.disabled = true;
  setFeedback("Erzeuge Zusammenfassung…");
  try {
    const data = await api("/api/summary", {
      wahlperiode: periodValue(),
      language: "de",
    });
    renderDistribution(data.distribution || data);
    $("#summaryBox").hidden = false;
    $("#summaryText").textContent = data.summary || "";
    setFeedback("Zusammenfassung bereit.");
  } catch (err) {
    setFeedback(err.message, true);
  } finally {
    button.disabled = false;
  }
});

function selectedChatMode() {
  const checked = document.querySelector('input[name="chatMode"]:checked');
  return checked ? checked.value : "direct";
}

$("#chatForm").addEventListener("submit", async (event) => {
  event.preventDefault();
  const input = $("#chatInput");
  const question = input.value.trim();
  if (!question) return;
  const mode = selectedChatMode();
  appendBubble("user", question);
  input.value = "";
  const button = event.submitter;
  button.disabled = true;
  try {
    const data = await api("/api/chat", { question, mode });
    appendBubble("agent", data.answer);
    const tools =
      data.tools_called && data.tools_called.length
        ? data.tools_called.join(", ")
        : "—";
    $("#chatEngine").textContent =
      `Antwort-Engine: ${data.engine} · Pfad: ${data.path} · Tools: ${tools}`;
  } catch (err) {
    appendBubble("agent", `Fehler: ${err.message}`);
  } finally {
    button.disabled = false;
    input.focus();
  }
});

loadHealth();
