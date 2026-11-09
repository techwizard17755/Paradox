const app = document.getElementById("app");
const toast = document.getElementById("toast");
const letters = ["A", "B", "C", "D"];
const localQuizLibraryKey = "paradox-local-quizzes";
const localQuizMigrationKey = "paradox-local-quizzes-migrated";
let questions = [newQuestion()];
let pollingTimer = null;
let currentMode = null;
let pollInProgress = false;
let lastSnapshotSignature = null;
let savedQuizId = null;
let currentTheme = "violet";
let toastTimer = null;
const quizThemes = [
  { id: "violet", name: "Dream", color: "#a98bff" },
  { id: "ocean", name: "Ocean", color: "#66e4d1" },
  { id: "citrus", name: "Citrus", color: "#d3ff72" },
  { id: "coral", name: "Coral", color: "#ff887c" },
  { id: "sunset", name: "Sunset", color: "#ffb36b" }
];

function newQuestion() {
  return { prompt: "", options: ["", ""], correctOptionIndex: 0, timeLimitSeconds: 20 };
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[character]);
}

function notify(message) {
  toast.textContent = message;
  toast.classList.add("visible");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("visible"), 3200);
}

async function api(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options.headers || {}) }
  });
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(body?.detail || body?.title || "Something went wrong. Please try again.");
    error.status = response.status;
    throw error;
  }
  return body;
}

function post(url, body, headers = {}) {
  return api(url, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body), headers });
}

function setNav(active) {
  document.querySelectorAll(".nav-link").forEach(button => {
    button.classList.toggle("active", button.dataset.page === active);
  });
}

function setTheme(theme = "violet") {
  app.dataset.theme = quizThemes.some(candidate => candidate.id === theme) ? theme : "violet";
}

function quizKey() {
  return localStorage.getItem("paradox-quiz-key");
}

function ownerHeaders() {
  const key = quizKey();
  return key ? { "X-Quiz-Key": key } : {};
}

function usesBrowserQuizLibrary() {
  return !["localhost", "127.0.0.1"].includes(window.location.hostname);
}

function readLocalQuizzes() {
  const stored = localStorage.getItem(localQuizLibraryKey);
  if (!stored) return [];

  let quizzes;
  try {
    quizzes = JSON.parse(stored);
  } catch {
    throw new Error("Your saved quizzes could not be read from this browser.");
  }
  if (!Array.isArray(quizzes) ||
      quizzes.some(quiz => !quiz || typeof quiz.id !== "string" ||
        typeof quiz.quizName !== "string" || !Array.isArray(quiz.questions))) {
    throw new Error("Your saved quiz library has an invalid format.");
  }
  return quizzes;
}

function writeLocalQuizzes(quizzes) {
  try {
    localStorage.setItem(localQuizLibraryKey, JSON.stringify(quizzes));
  } catch (error) {
    if (error instanceof DOMException && error.name === "QuotaExceededError")
      throw new Error("This browser is out of storage space. Remove unused site data and try again.");
    throw error;
  }
}

async function loadLocalQuizzes() {
  const localQuizzes = readLocalQuizzes();
  const ownerKey = quizKey();
  if (!ownerKey || localStorage.getItem(localQuizMigrationKey) === "true")
    return localQuizzes;

  try {
    const remoteQuizzes = await api("/api/library/", { headers: ownerHeaders() });
    const localIds = new Set(localQuizzes.map(quiz => quiz.id));
    const importedQuizzes = remoteQuizzes.filter(quiz => !localIds.has(quiz.id));
    const mergedQuizzes = [...localQuizzes, ...importedQuizzes];
    writeLocalQuizzes(mergedQuizzes);
    localStorage.setItem(localQuizMigrationKey, "true");
    localStorage.removeItem("paradox-quiz-key");
    if (importedQuizzes.length)
      notify(`Imported ${importedQuizzes.length} saved ${importedQuizzes.length === 1 ? "quiz" : "quizzes"} to this browser.`);
    return mergedQuizzes;
  } catch (error) {
    if (error.status === 401) {
      localStorage.removeItem("paradox-quiz-key");
      localStorage.setItem(localQuizMigrationKey, "true");
      return localQuizzes;
    }
    throw new Error(`Could not import your existing saved quizzes: ${error.message}`);
  }
}

function stopPolling() {
  clearInterval(pollingTimer);
  pollingTimer = null;
  currentMode = null;
  pollInProgress = false;
  lastSnapshotSignature = null;
}

function renderJoin() {
  stopPolling();
  setTheme();
  setNav("join");
  app.innerHTML = `
    <section class="hero">
      <div class="decor-board" aria-hidden="true">
        <div class="quiz-preview">
          <div class="preview-line"><span>QUICK QUIZ</span><span class="preview-tag">LIVE</span></div>
          <div class="preview-question">Which one doesn't belong?</div>
          <div class="preview-answer">A. A quiet library</div>
          <div class="preview-answer correct">B. A loud concert</div>
          <div class="preview-answer">C. A sleeping cat</div>
        </div>
        <div class="float-tile tile-one">✦</div><div class="float-tile tile-two">◈</div><div class="float-tile tile-three">✧</div>
      </div>
      <div class="hero-copy-block">
        <p class="eyebrow"><span class="eyebrow-mark">✦</span> A quiz night, reimagined</p>
        <h1>Think fast.<br><em>Have fun.</em></h1>
        <p class="hero-copy">Jump into a live quiz with your crew. Pick a nickname, bring your best guesses, and see who comes out on top.</p>
        <div class="hero-actions">
          <button class="primary-btn" data-action="focus-pin">Enter a game PIN <span aria-hidden="true">→</span></button>
          <button class="secondary-btn" data-page="create">Make your own ✦</button>
        </div>
        <div class="social-proof"><div class="avatar-stack"><span class="avatar">J</span><span class="avatar">M</span><span class="avatar">A</span></div><span>Better together, one question at a time</span></div>
      </div>
      <form class="join-card" data-form="pin">
        <span class="card-kicker">Ready when you are</span>
        <h2>Join the game</h2>
        <p>Ask your host for the six-digit PIN and you’re in.</p>
        <label class="field-label" for="game-pin">Game PIN</label>
        <input id="game-pin" class="pin-input" name="pin" inputmode="numeric" autocomplete="one-time-code" maxlength="6" placeholder="000 000" required aria-label="Six-digit game PIN">
        <button class="primary-btn" type="submit">Let’s play <span aria-hidden="true">→</span></button>
        <div class="join-footnote"><span aria-hidden="true">✦</span> No account needed. Just a little friendly competition.</div>
      </form>
    </section>`;
}

function syncQuestionsFromForm() {
  const cards = [...document.querySelectorAll(".question-editor")];
  if (!cards.length) return;
  questions = cards.map((card, index) => {
    const options = [...card.querySelectorAll(".option-field")].map(input => input.value);
    const selected = card.querySelector('input[type="radio"]:checked');
    return {
      prompt: card.querySelector(".question-prompt").value,
      options,
      correctOptionIndex: selected ? Number(selected.value) : 0,
      timeLimitSeconds: Number(card.querySelector(".timer-select").value)
    };
  });
}

function renderBuilder(quiz = null) {
  stopPolling();
  setTheme(quiz?.theme || "violet");
  setNav("create");
  savedQuizId = quiz?.id || null;
  currentTheme = quiz?.theme || "violet";
  const localLibrary = usesBrowserQuizLibrary();
  app.innerHTML = `
    <div class="content-heading">
      <p class="eyebrow"><span class="eyebrow-mark">✦</span> Your quiz, your rules</p>
      <h1>Make a little magic.</h1>
      <p>Write questions, choose the right answers, then host a live game your friends can join with one PIN.</p>
    </div>
    <section class="builder-layout">
      <form class="builder-main" data-form="create">
        <label class="field-label" for="quiz-name">Give your quiz a name</label>
        <input id="quiz-name" class="text-field" name="quizName" maxlength="80" value="${escapeHtml(quiz?.quizName || "")}" placeholder="e.g. The Great Movie Mix-Up" required>
        <label class="field-label" for="quiz-description" style="margin-top:17px">Add a short introduction <span style="color:var(--dim);font-weight:500">(optional)</span></label>
        <textarea id="quiz-description" class="text-field quiz-description" name="description" maxlength="280" placeholder="Set the scene before everyone plays…">${escapeHtml(quiz?.description || "")}</textarea>
        <span class="field-label" style="margin-top:18px">Pick your quiz look</span>
        <div class="theme-options" role="radiogroup" aria-label="Quiz color theme">
          ${quizThemes.map(theme => `
            <label class="theme-choice" style="--swatch:${theme.color}">
              <input type="radio" name="theme" value="${theme.id}" ${theme.id === currentTheme ? "checked" : ""}>
              <span class="theme-swatch">${theme.name}</span>
            </label>`).join("")}
        </div>
        <div id="question-list" class="question-list"></div>
        <div class="builder-actions">
          <button class="secondary-btn" type="button" data-action="add-question"><span aria-hidden="true">＋</span> Add a question</button>
          <span class="save-note">${savedQuizId ? "Editing a saved quiz" : localLibrary ? "Saves in this browser" : "Saves to your quiz library"}</span>
          <div style="display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end">
            <button class="secondary-btn" type="button" data-action="save-quiz">Save quiz</button>
            <button class="primary-btn" type="submit" data-intent="host">Host quiz <span aria-hidden="true">→</span></button>
          </div>
        </div>
      </form>
      <aside class="builder-aside">
        <span class="card-kicker">Quick tips</span>
        <h3>Make it a moment.</h3>
        <p>A great quiz is easy to jump into and a little hard to forget.</p>
        <div class="aside-divider"></div>
        <div class="tip-row"><span class="tip-icon">✓</span><span>Keep each question short and snappy.</span></div>
        <div class="tip-row"><span class="tip-icon">◷</span><span>Pick a timer that keeps the room buzzing.</span></div>
        <div class="tip-row"><span class="tip-icon">✦</span><span>Tap the circle beside the right answer.</span></div>
        <div class="tip-row"><span class="tip-icon">🎨</span><span>Choose a color palette and add a welcome note.</span></div>
        <div class="aside-divider"></div>
        <p>${localLibrary
          ? 'Your quizzes are saved on this device in this browser. They are not synced to other devices.'
          : 'Your quizzes are saved privately in <strong style="color:var(--lime)">My quizzes</strong>, so you can edit or host them later.'}</p>
      </aside>
    </section>`;
  drawQuestionCards();
}

function quizPayload(form) {
  syncQuestionsFromForm();
  const data = new FormData(form);
  return {
    quizName: data.get("quizName").toString().trim(),
    description: data.get("description").toString().trim(),
    theme: data.get("theme")?.toString() || currentTheme,
    questions
  };
}

async function saveCurrentQuiz(form) {
  const payload = quizPayload(form);
  if (usesBrowserQuizLibrary()) {
    const quizzes = readLocalQuizzes();
    const existing = quizzes.find(quiz => quiz.id === savedQuizId);
    const now = new Date().toISOString();
    const quiz = {
      id: savedQuizId || crypto.randomUUID(),
      ...payload,
      createdAt: existing?.createdAt || now,
      updatedAt: now
    };
    writeLocalQuizzes([quiz, ...quizzes.filter(item => item.id !== quiz.id)]);
    savedQuizId = quiz.id;
    currentTheme = quiz.theme;
    notify("Quiz saved in this browser.");
    return quiz;
  }

  const headers = ownerHeaders();
  const saved = savedQuizId
    ? await api(`/api/library/${encodeURIComponent(savedQuizId)}`, {
      method: "PUT", body: JSON.stringify(payload), headers: { "Content-Type": "application/json", ...headers }
    })
    : await post("/api/library/", payload, headers);
  if (saved.ownerKey) localStorage.setItem("paradox-quiz-key", saved.ownerKey);
  const quiz = saved.quiz || saved;
  savedQuizId = quiz.id;
  currentTheme = quiz.theme;
  notify("Quiz saved to My quizzes.");
  return quiz;
}

async function renderLibrary() {
  stopPolling();
  setTheme();
  setNav("library");
  const localLibrary = usesBrowserQuizLibrary();
  const key = quizKey();
  if (!localLibrary && !key) {
    app.innerHTML = `
      <div class="library-header content-heading">
        <div><p class="eyebrow"><span class="eyebrow-mark">✦</span> Your quiz studio</p><h1>Your quizzes.</h1><p>Saved quizzes will live here, ready to edit or host again whenever you're ready.</p></div>
        <button class="primary-btn" data-page="create">Create a quiz <span aria-hidden="true">→</span></button>
      </div>
      <div class="library-empty">Create your first quiz and choose <strong>Save quiz</strong> to keep it here.</div>`;
    return;
  }
  app.innerHTML = `
    <div class="library-header content-heading">
      <div><p class="eyebrow"><span class="eyebrow-mark">✦</span> Your quiz studio</p><h1>Your quizzes.</h1><p>${localLibrary
        ? "Saved on this device in this browser. Use the same browser and website to find them again."
        : "Pick up where you left off. Edit, customize, or host any quiz in your library."}</p></div>
      <button class="primary-btn" data-page="create">Create a quiz <span aria-hidden="true">→</span></button>
    </div>
    <div id="quiz-library" class="library-grid"><div class="library-empty">Loading your saved quizzes…</div></div>`;
  try {
    const savedQuizzes = localLibrary
      ? await loadLocalQuizzes()
      : await api("/api/library/", { headers: ownerHeaders() });
    const grid = document.getElementById("quiz-library");
    if (!grid) return;
    grid.innerHTML = savedQuizzes.length
      ? savedQuizzes.map(quiz => {
        const theme = quizThemes.find(item => item.id === quiz.theme) || quizThemes[0];
        return `<article class="library-card">
          <div class="library-card-accent" style="--card-accent:${theme.color}"></div>
          <div class="library-card-content">
            <h2>${escapeHtml(quiz.quizName)}</h2>
            <p>${escapeHtml(quiz.description || "A little quiz, ready for game night.")}</p>
            <div class="library-card-meta"><span>${quiz.questions.length} questions</span><span>${theme.name} theme</span></div>
            <div class="library-card-actions">
              <button class="primary-btn" data-action="host-saved" data-id="${escapeHtml(quiz.id)}">Host</button>
              <button class="secondary-btn" data-action="edit-saved" data-id="${escapeHtml(quiz.id)}">Edit</button>
              <button class="quiet-btn" data-action="delete-saved" data-id="${escapeHtml(quiz.id)}" aria-label="Delete ${escapeHtml(quiz.quizName)}">Delete</button>
            </div>
          </div>
        </article>`;
      }).join("")
      : `<div class="library-empty">Your library is ready. Save a quiz from the Create page and it will appear here.</div>`;
  } catch (error) {
    if (!localLibrary && error.message.includes("key")) {
      localStorage.removeItem("paradox-quiz-key");
      renderLibrary();
    } else {
      document.getElementById("quiz-library").innerHTML = `<div class="library-empty">${escapeHtml(error.message)}</div>`;
    }
  }
}

function drawQuestionCards() {
  const list = document.getElementById("question-list");
  if (!list) return;
  list.innerHTML = questions.map((question, questionIndex) => `
    <article class="question-editor" data-question="${questionIndex}">
      <div class="question-editor-head">
        <span class="question-editor-title">Question ${questionIndex + 1}</span>
        ${questions.length > 1 ? `<button type="button" class="remove-question" data-action="remove-question" data-index="${questionIndex}">Remove</button>` : ""}
      </div>
      <label class="field-label" for="prompt-${questionIndex}">Question</label>
      <textarea id="prompt-${questionIndex}" class="question-prompt" maxlength="240" placeholder="Write something fun to figure out…" required>${escapeHtml(question.prompt)}</textarea>
      <div class="options-heading"><span>Answer choices</span><span>Mark the correct one</span></div>
      <div class="options-grid">
        ${question.options.map((option, optionIndex) => `
          <label class="option-row">
            <input class="correct-radio" type="radio" name="correct-${questionIndex}" value="${optionIndex}" ${optionIndex === question.correctOptionIndex ? "checked" : ""} aria-label="Mark answer ${letters[optionIndex]} as correct">
            <input class="option-field" maxlength="100" placeholder="${letters[optionIndex]}. Answer choice" value="${escapeHtml(option)}" required>
          </label>`).join("")}
      </div>
      <div class="question-footer">
        ${question.options.length < 4 ? `<button type="button" class="quiet-btn" data-action="add-option" data-index="${questionIndex}">＋ Add choice</button>` : `<span></span>`}
        <label class="field-label" style="display:flex;align-items:center;gap:8px;margin:0">Timer
          <select class="timer-select" aria-label="Question timer">
            ${[10, 15, 20, 30, 45, 60].map(seconds => `<option value="${seconds}" ${seconds === question.timeLimitSeconds ? "selected" : ""}>${seconds} sec</option>`).join("")}
          </select>
        </label>
      </div>
    </article>`).join("");
}

function renderUsername(pin) {
  stopPolling();
  setTheme();
  setNav("join");
  app.innerHTML = `
    <div class="username-card join-card">
      <span class="card-kicker">PIN ${escapeHtml(pin)} is ready</span>
      <h2>What should we call you?</h2>
      <p>Pick a name your friends will recognize on the scoreboard.</p>
      <form data-form="username">
        <label class="field-label" for="username">Your nickname</label>
        <input id="username" class="text-field" name="username" maxlength="20" placeholder="e.g. Quiz Wizard" autocomplete="nickname" required>
        <button class="primary-btn" type="submit">Join the lobby <span aria-hidden="true">→</span></button>
      </form>
      <button class="quiet-btn" data-action="back-to-join">← Use a different PIN</button>
    </div>`;
  document.getElementById("username").focus();
}

function renderHost(snapshot) {
  const host = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
  if (!host) return renderJoin();
  setTheme(snapshot.theme);
  setNav("");
  const lobby = snapshot.status === "Lobby";
  const finished = snapshot.status === "Finished";
  const title = finished ? "That’s a wrap!" : snapshot.quizName;
  const players = snapshot.players || [];
  const scoreList = players.length
    ? `<ol class="score-list">${players.map((player, index) => `<li class="score-row"><span>${index === 0 && player.score > 0 ? "✦ " : ""}${escapeHtml(player.username)}</span><strong>${player.score.toLocaleString()} pts</strong></li>`).join("")}</ol>`
    : `<div class="empty-state">Your players will show up here.</div>`;
  const isPublicHost = usesBrowserQuizLibrary();
  app.innerHTML = `
    <section class="game-layout">
      <div class="game-header"><h1>${escapeHtml(title)}</h1><div class="game-pin"><span>JOIN PIN</span><strong>${escapeHtml(snapshot.pin)}</strong></div></div>
      ${lobby ? `
        <div class="lobby-layout">
          <section class="waiting-panel">
            <div class="game-meta"><span>THE LOBBY</span><strong>${snapshot.playerCount} ${snapshot.playerCount === 1 ? "player" : "players"}</strong></div>
            <h2 style="margin-top:22px">The room is open.</h2>
            <p>${escapeHtml(snapshot.description || "Share the PIN with your crew. When everyone’s in, start the quiz.")}</p>
            <div class="player-roster">${players.length ? players.map(player => `<div class="roster-item"><span class="roster-avatar">${escapeHtml(player.username.slice(0, 1).toUpperCase())}</span><span>${escapeHtml(player.username)}</span></div>`).join("") : `<div class="empty-state">Waiting for the first player to join…</div>`}</div>
          </section>
          <aside class="host-actions">
            <div class="waiting-panel room-code"><span class="code-label">Your game PIN</span><strong class="big-code">${escapeHtml(snapshot.pin)}</strong><span class="copy-note">Share this code so players can join.</span>${isPublicHost ? `<button class="secondary-btn" data-action="copy-invite" style="margin-top:13px;width:100%">Copy player website</button>` : ""}</div>
            <button class="primary-btn" data-action="start" ${snapshot.playerCount === 0 ? "disabled" : ""}>Start the quiz <span aria-hidden="true">→</span></button>
            <button class="quiet-btn" data-action="leave">← Back to Paradox</button>
          </aside>
        </div>
      ` : finished ? `
        <div class="game-panel">
          <div class="reveal-banner">Quiz complete — nice playing, everyone!</div>
          <h2 style="text-align:center;margin:0 0 18px;font-size:21px">Final leaderboard</h2>
          ${scoreList}
          <div style="display:flex;justify-content:center;margin-top:22px"><button class="primary-btn" data-action="new-quiz">Create another quiz</button></div>
        </div>
      ` : `
        <div class="game-panel">
          <div class="game-meta"><span>${snapshot.status === "Reveal" ? "ANSWER REVEAL" : "QUESTION " + snapshot.questionNumber + " OF " + snapshot.questionCount}</span><strong class="question-clock">${snapshot.status === "Question" ? secondsLeft(snapshot.endsAtUtc) + " sec" : snapshot.playerCount + " playing"}</strong></div>
          <div class="timer-track"><div class="timer-fill" style="width:${timerPercent(snapshot)}%"></div></div>
          <h2 class="game-question">${escapeHtml(snapshot.currentQuestion?.prompt)}</h2>
          <div class="answer-grid">${(snapshot.currentQuestion?.options || []).map((option, index) => `<div class="answer-card ${snapshot.status === "Reveal" ? (index === snapshot.correctOptionIndex ? "is-correct" : "is-wrong") : ""}"><span class="answer-symbol">${letters[index]}</span><span>${escapeHtml(option)}</span></div>`).join("")}</div>
          ${snapshot.status === "Reveal" ? `<div class="reveal-banner">${snapshot.playerCount} ${snapshot.playerCount === 1 ? "player" : "players"} in the game</div>` : `<p class="answer-status">${players.filter(player => player.lastAnswerPoints !== null).length} of ${snapshot.playerCount} answered</p>`}
          ${snapshot.status === "Reveal" ? `<div style="display:flex;justify-content:center;margin:20px 0 27px"><button class="primary-btn" data-action="next">${snapshot.questionNumber >= snapshot.questionCount ? "See final scores" : "Next question"} <span aria-hidden="true">→</span></button></div>` : ""}
          <div class="aside-divider"></div><h3 style="font-size:14px">Live leaderboard</h3>${scoreList}
        </div>
      `}
    </section>`;
}

function renderPlayer(snapshot) {
  const player = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
  if (!player) return renderJoin();
  setTheme(snapshot.theme);
  setNav("");
  const lobby = snapshot.status === "Lobby";
  const finished = snapshot.status === "Finished";
  const players = snapshot.players || [];
  const scoreList = players.length
    ? `<ol class="score-list">${players.map((entry, index) => `<li class="score-row"><span>${index === 0 && entry.score > 0 ? "✦ " : ""}${escapeHtml(entry.username)}</span><strong>${entry.score.toLocaleString()} pts</strong></li>`).join("")}</ol>`
    : "";
  app.innerHTML = `
    <section class="game-layout">
      <div class="game-header"><h1>${escapeHtml(snapshot.quizName)}</h1><div class="game-pin"><span>YOU’RE IN</span><strong>${escapeHtml(player.username)}</strong></div></div>
      ${lobby ? `
        <div class="waiting-panel" style="text-align:center;padding:56px 25px">
          <span class="eyebrow">✦ You made it</span><h2 style="font-size:28px">You’re in the lobby, ${escapeHtml(player.username)}.</h2>
          <p>Keep this page open. Your host will start the first question soon.</p>
          <div class="avatar-stack" style="justify-content:center;margin:22px">${players.map(entry => `<span class="avatar" title="${escapeHtml(entry.username)}">${escapeHtml(entry.username.slice(0, 1).toUpperCase())}</span>`).join("")}</div>
          <span style="color:var(--dim);font-size:11px">${snapshot.playerCount} players in the room</span>
        </div>
      ` : finished ? `
        <div class="game-panel">
          <div class="reveal-banner">That’s the end — thanks for playing!</div>
          <h2 style="text-align:center;margin:0 0 8px;font-size:23px">${escapeHtml(player.username)}, you scored ${players.find(entry => entry.username === player.username)?.score?.toLocaleString() || 0} points.</h2>
          <p style="text-align:center;color:var(--muted);font-size:12px;margin-bottom:22px">Final leaderboard</p>${scoreList}
          <div style="display:flex;justify-content:center;margin-top:24px"><button class="secondary-btn" data-action="leave">Back to Paradox</button></div>
        </div>
      ` : `
        <div class="game-panel">
          <div class="game-meta"><span>${snapshot.status === "Reveal" ? "ANSWER REVEAL" : "QUESTION " + snapshot.questionNumber + " OF " + snapshot.questionCount}</span><strong class="question-clock">${snapshot.status === "Question" ? secondsLeft(snapshot.endsAtUtc) + " sec" : "SCORES"}</strong></div>
          <div class="timer-track"><div class="timer-fill" style="width:${timerPercent(snapshot)}%"></div></div>
          <h2 class="game-question">${escapeHtml(snapshot.currentQuestion?.prompt)}</h2>
          <div class="answer-grid">${(snapshot.currentQuestion?.options || []).map((option, index) => `<button class="answer-card ${snapshot.myAnswerIndex === index ? "selected" : ""} ${snapshot.status === "Reveal" ? (index === snapshot.correctOptionIndex ? "is-correct" : (snapshot.myAnswerIndex === index ? "is-wrong" : "")) : ""}" data-action="answer" data-index="${index}" ${snapshot.status !== "Question" || snapshot.myAnswerIndex !== null ? "disabled" : ""}><span class="answer-symbol">${letters[index]}</span><span>${escapeHtml(option)}</span></button>`).join("")}</div>
          <p class="answer-status">${snapshot.status === "Reveal" ? snapshot.myAnswerIndex === snapshot.correctOptionIndex ? `You got it! +${players.find(entry => entry.username === player.username)?.lastAnswerPoints || 0} points ✦` : "The correct answer is highlighted above." : snapshot.myAnswerIndex !== null ? "Answer locked in — nice one. Hang tight for the reveal." : "Choose your answer before the timer runs out."}</p>
          ${snapshot.status === "Reveal" ? `<div class="reveal-banner">Question ${snapshot.questionNumber} of ${snapshot.questionCount} · Scores updated</div><h3 style="font-size:14px">Leaderboard</h3>${scoreList}` : ""}
        </div>
      `}
    </section>`;
}

function secondsLeft(endTime) {
  if (!endTime) return 0;
  return Math.max(0, Math.ceil((new Date(endTime).getTime() - Date.now()) / 1000));
}

function timerPercent(snapshot) {
  if (snapshot.status !== "Question" || !snapshot.currentQuestion) return 0;
  return Math.max(0, Math.min(100, secondsLeft(snapshot.endsAtUtc) / snapshot.currentQuestion.timeLimitSeconds * 100));
}

function updateClock(snapshot) {
  const clock = document.querySelector(".question-clock");
  const fill = document.querySelector(".timer-fill");
  if (clock) {
    clock.textContent = snapshot.status !== "Question"
      ? currentMode?.type === "host" ? `${snapshot.playerCount} playing` : "SCORES"
      : `${secondsLeft(snapshot.endsAtUtc)} sec`;
  }
  if (fill) fill.style.width = `${timerPercent(snapshot)}%`;
}

function snapshotSignature(snapshot) {
  return JSON.stringify([
    snapshot.status,
    snapshot.playerCount,
    snapshot.players,
    snapshot.currentQuestion,
    snapshot.questionNumber,
    snapshot.questionCount,
    snapshot.theme,
    snapshot.description,
    snapshot.endsAtUtc,
    snapshot.correctOptionIndex,
    snapshot.myAnswerIndex
  ]);
}

async function refreshRoom() {
  if (!currentMode || pollInProgress) return;
  pollInProgress = true;
  const mode = currentMode;
  try {
    if (mode.type === "host") {
      const snapshot = await api(`/api/games/${encodeURIComponent(mode.room.pin)}`, { headers: { "X-Host-Key": mode.room.hostKey } });
      if (currentMode === mode) {
        const signature = snapshotSignature(snapshot);
        if (signature !== lastSnapshotSignature) {
          lastSnapshotSignature = signature;
          renderHost(snapshot);
        }
        updateClock(snapshot);
      }
    } else {
      const snapshot = await api(`/api/games/${encodeURIComponent(mode.room.pin)}`, { headers: { "X-Player-Id": mode.room.playerId } });
      if (currentMode === mode) {
        const signature = snapshotSignature(snapshot);
        if (signature !== lastSnapshotSignature) {
          lastSnapshotSignature = signature;
          renderPlayer(snapshot);
        }
        updateClock(snapshot);
      }
    }
  } catch (error) {
    if (currentMode === mode) {
      stopPolling();
      sessionStorage.removeItem(mode.type === "host" ? "paradox-host" : "paradox-player");
      renderJoin();
      notify(error.message);
    }
  } finally {
    pollInProgress = false;
  }
}

function startPolling(type, room) {
  stopPolling();
  currentMode = { type, room };
  refreshRoom();
  pollingTimer = setInterval(refreshRoom, 900);
}

function restoreSession() {
  const host = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
  if (host) return startPolling("host", host);
  const player = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
  if (player) return startPolling("player", player);
  renderJoin();
}

document.addEventListener("click", async event => {
  const pageButton = event.target.closest("[data-page]");
  if (pageButton) {
    if (pageButton.dataset.page === "create") {
      questions = [newQuestion()];
      renderBuilder();
    } else if (pageButton.dataset.page === "library") {
      renderLibrary();
    } else {
      renderJoin();
    }
    return;
  }

  const actionButton = event.target.closest("[data-action]");
  if (!actionButton) return;
  const action = actionButton.dataset.action;

  if (action === "focus-pin") {
    document.getElementById("game-pin")?.focus();
    return;
  }
  if (action === "copy-invite") {
    try {
      await navigator.clipboard.writeText(window.location.origin);
      notify("Player website copied. Share it with the game PIN.");
    } catch (error) {
      notify("Copy the player website from your browser address bar and share it with the game PIN.");
    }
    return;
  }
  if (action === "save-quiz") {
    const form = document.querySelector('form[data-form="create"]');
    if (!form?.reportValidity()) return;
    actionButton.disabled = true;
    try {
      await saveCurrentQuiz(form);
    } catch (error) {
      notify(error.message);
    } finally {
      actionButton.disabled = false;
    }
    return;
  }
  if (action === "edit-saved" || action === "host-saved") {
    try {
      const quiz = usesBrowserQuizLibrary()
        ? readLocalQuizzes().find(item => item.id === actionButton.dataset.id)
        : await api(`/api/library/${encodeURIComponent(actionButton.dataset.id)}`, { headers: ownerHeaders() });
      if (!quiz) throw new Error("That saved quiz could not be found.");
      if (action === "edit-saved") {
        questions = quiz.questions;
        renderBuilder(quiz);
      } else {
        await hostQuiz(quiz);
      }
    } catch (error) {
      notify(error.message);
      if (error.message.includes("key")) renderLibrary();
    }
    return;
  }
  if (action === "delete-saved") {
    if (!confirm("Delete this saved quiz? This cannot be undone.")) return;
    try {
      if (usesBrowserQuizLibrary()) {
        const quizzes = readLocalQuizzes().filter(quiz => quiz.id !== actionButton.dataset.id);
        writeLocalQuizzes(quizzes);
      } else {
        await api(`/api/library/${encodeURIComponent(actionButton.dataset.id)}`, {
          method: "DELETE", headers: ownerHeaders()
        });
      }
      notify("Quiz deleted.");
      await renderLibrary();
    } catch (error) {
      notify(error.message);
    }
    return;
  }
  if (action === "add-question") {
    syncQuestionsFromForm();
    if (questions.length >= 30) return notify("A quiz can have up to 30 questions.");
    questions.push(newQuestion());
    drawQuestionCards();
    document.querySelector(`[data-question="${questions.length - 1}"] .question-prompt`)?.focus();
    return;
  }
  if (action === "remove-question") {
    syncQuestionsFromForm();
    questions.splice(Number(actionButton.dataset.index), 1);
    drawQuestionCards();
    return;
  }
  if (action === "add-option") {
    syncQuestionsFromForm();
    const question = questions[Number(actionButton.dataset.index)];
    if (question.options.length < 4) question.options.push("");
    drawQuestionCards();
    return;
  }
  if (action === "back-to-join" || action === "leave") {
    sessionStorage.removeItem("paradox-player");
    sessionStorage.removeItem("paradox-host");
    renderJoin();
    return;
  }
  if (action === "new-quiz") {
    sessionStorage.removeItem("paradox-host");
    questions = [newQuestion()];
    renderBuilder();
    return;
  }
  if (action === "start") {
    const room = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
    try {
      await post(`/api/games/${room.pin}/start`, undefined, { "X-Host-Key": room.hostKey });
      await refreshRoom();
    } catch (error) { notify(error.message); }
    return;
  }
  if (action === "next") {
    const room = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
    try {
      await post(`/api/games/${room.pin}/next`, undefined, { "X-Host-Key": room.hostKey });
      await refreshRoom();
    } catch (error) { notify(error.message); }
    return;
  }
  if (action === "answer") {
    const room = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
    actionButton.disabled = true;
    try {
      await post(`/api/games/${room.pin}/answers`, { optionIndex: Number(actionButton.dataset.index) }, { "X-Player-Id": room.playerId });
      await refreshRoom();
    } catch (error) { notify(error.message); }
  }
});

document.addEventListener("input", event => {
  if (event.target.id === "game-pin") event.target.value = event.target.value.replace(/\D/g, "").slice(0, 6);
});

document.addEventListener("change", event => {
  if (event.target.matches('input[name="theme"]')) {
    currentTheme = event.target.value;
    setTheme(currentTheme);
  }
});

async function hostQuiz(quiz) {
  const created = await post("/api/games", {
    quizName: quiz.quizName,
    description: quiz.description,
    theme: quiz.theme,
    questions: quiz.questions
  });
  sessionStorage.removeItem("paradox-player");
  const room = { pin: created.pin, hostKey: created.hostKey };
  sessionStorage.setItem("paradox-host", JSON.stringify(room));
  startPolling("host", room);
}

document.addEventListener("submit", async event => {
  const form = event.target.closest("form[data-form]");
  if (!form) return;
  event.preventDefault();

  if (form.dataset.form === "pin") {
    const pin = new FormData(form).get("pin").toString().trim();
    if (!/^\d{6}$/.test(pin)) return notify("Enter the six-digit PIN from your host.");
    try {
      const snapshot = await api(`/api/games/${encodeURIComponent(pin)}`);
      if (snapshot.status !== "Lobby") return notify("This game has already started. Ask your host for a new PIN.");
      renderUsername(pin);
    } catch (error) { notify(error.message); }
    return;
  }

  if (form.dataset.form === "username") {
    const pin = document.querySelector(".card-kicker").textContent.match(/\d{6}/)?.[0];
    if (!pin) return renderJoin();
    const username = new FormData(form).get("username").toString().trim();
    if (!username) return notify("Add a nickname before joining.");
    const button = form.querySelector('button[type="submit"]');
    button.disabled = true;
    try {
      const joined = await post(`/api/games/${pin}/players`, { username });
      sessionStorage.removeItem("paradox-host");
      const room = { pin: joined.pin, playerId: joined.playerId, username: joined.username };
      sessionStorage.setItem("paradox-player", JSON.stringify(room));
      startPolling("player", room);
    } catch (error) {
      button.disabled = false;
      notify(error.message);
    }
    return;
  }

  if (form.dataset.form === "create") {
    const button = event.submitter || form.querySelector('button[type="submit"]');
    button.disabled = true;
    try {
      const quiz = await saveCurrentQuiz(form);
      await hostQuiz(quiz);
    } catch (error) {
      button.disabled = false;
      notify(error.message);
    }
  }
});

restoreSession();
