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
let previousRoomSnapshot = null;
let savedQuizId = null;
let builderDraft = null;
let currentTheme = "violet";
let showMode = "Quiz";
let boardEnabled = false;
let subjectWheelEnabled = false;
let boardTiles = defaultBoardTiles();
let boardTitle = "The Showdown";
let boardLayout = "Track";
let boardStyle = "Aurora";
let toastTimer = null;
let wheelAnimationUntil = 0;
let pendingWheelResult = "";
const quizThemes = [
  { id: "violet", name: "Dream", color: "#a98bff" },
  { id: "ocean", name: "Ocean", color: "#66e4d1" },
  { id: "citrus", name: "Citrus", color: "#d3ff72" },
  { id: "coral", name: "Coral", color: "#ff887c" },
  { id: "sunset", name: "Sunset", color: "#ffb36b" }
];
const wheelColors = ["#d3ff72", "#a98bff", "#66e4d1", "#ff887c", "#ffb36b", "#69a8ff", "#f477d0", "#b8ec6b"];
const showModeOptions = [
  ["Board", "Digital board game", "Players take turns rolling, moving, and resolving your custom tiles.", "▦"],
  ["Quiz", "Quiz game", "Play questions together, with an optional subject wheel.", "✦"],
  ["Custom", "Custom", "A flexible builder for combining board and quiz mechanics.", "⚙"]
];

function newQuestion() {
  return {
    prompt: "",
    type: "MultipleChoice",
    subject: "General",
    options: ["", ""],
    correctOptionIndex: 0,
    acceptedAnswers: [""],
    timeLimitSeconds: 20
  };
}

function defaultBoardTiles() {
  return [
    { action: "Start", amount: 0, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Question", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Bonus", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Question", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Penalty", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Question", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "MoveForward", amount: 1, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Question", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "SkipTurn", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Question", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Bonus", amount: 200, subject: "", prompt: "", label: "", imageDataUrl: "" },
    { action: "Question", amount: 100, subject: "", prompt: "", label: "", imageDataUrl: "" }
  ];
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
  document.querySelectorAll(".nav-link").forEach(link => {
    const isActive = link.dataset.page === active;
    link.classList.toggle("active", isActive);
    if (isActive) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  });
}

function modePath(mode) {
  return `/create/${mode.toLowerCase()}`;
}

function routeFor(pathname = window.location.pathname) {
  const path = pathname.replace(/\/+$/, "").toLowerCase() || "/";
  if (path === "/" || path === "/join") return { page: "join" };
  if (path === "/create") return { page: "create" };
  if (path === "/my-quizzes") return { page: "library" };

  const mode = path.match(/^\/create\/(board|quiz|custom)$/);
  if (mode) return { page: "builder", mode: mode[1][0].toUpperCase() + mode[1].slice(1) };

  const room = path.match(/^\/(host|play)\/(\d{6})$/);
  if (room) return { page: room[1], pin: room[2] };
  return { page: "not-found" };
}

function setPath(path, replace = false) {
  if (window.location.pathname === path && !window.location.search && !window.location.hash) return;
  if (replace) window.history.replaceState(null, "", path);
  else window.history.pushState(null, "", path);
}

function navigateTo(path, replace = false) {
  setPath(path, replace);
  renderRoute();
}

function renderRoute() {
  const route = routeFor();
  if (route.page === "join") return renderJoin();
  if (route.page === "create") return renderCreateChooser();
  if (route.page === "builder") return renderBuilder(null, route.mode);
  if (route.page === "library") return void renderLibrary();

  if (route.page === "host") {
    const host = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
    if (host?.pin === route.pin) return startPolling("host", host);
  }
  if (route.page === "play") {
    const player = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
    if (player?.pin === route.pin) return startPolling("player", player);
  }

  return navigateTo("/join", true);
}

function resetBuilder() {
  questions = [newQuestion()];
  savedQuizId = null;
  builderDraft = null;
  currentTheme = "violet";
  showMode = "Quiz";
  boardEnabled = false;
  subjectWheelEnabled = false;
  boardTiles = defaultBoardTiles();
  boardTitle = "The Showdown";
  boardLayout = "Track";
  boardStyle = "Aurora";
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
  previousRoomSnapshot = null;
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
          <a class="secondary-btn" href="/create" data-page="create" data-app-link>Make your own ✦</a>
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

function renderCreateChooser() {
  stopPolling();
  setTheme();
  setNav("create");
  app.innerHTML = `
    <div class="content-heading">
      <p class="eyebrow"><span class="eyebrow-mark">✦</span> Your show, your rules</p>
      <h1>What are we making?</h1>
      <p>Choose a format to open its own studio. Your show, your rules.</p>
    </div>
    <section class="show-mode-section format-picker" aria-labelledby="show-mode-heading">
      <div class="show-mode-heading">
        <span class="field-label">Choose your game show format</span>
        <h2 id="show-mode-heading">Start with a format</h2>
        <p>You can customize the questions and settings on the next page.</p>
      </div>
      <div class="show-mode-options" role="group" aria-label="Game show format">
        ${showModeOptions.map(([mode, title, description, icon]) => `
          <a class="show-mode-card" href="${modePath(mode)}" data-app-link>
            <span class="show-mode-icon" aria-hidden="true">${icon}</span>
            <span class="show-mode-card-title">${title}${mode === "Custom" ? `<span class="advanced-tag">Advanced · less recommended</span>` : ""}</span>
            <span class="show-mode-card-description">${description}</span>
            <span class="format-card-link">Open ${title} <span aria-hidden="true">→</span></span>
          </a>`).join("")}
      </div>
    </section>`;
}

function syncQuestionsFromForm() {
  const cards = [...document.querySelectorAll(".question-editor")];
  if (!cards.length) return;
  questions = cards.map((card, index) => {
    const existing = questions[index] || newQuestion();
    const type = card.querySelector(".question-type-select").value;
    const choiceFields = [...card.querySelectorAll(".answer-choice-field")];
    const acceptedFields = [...card.querySelectorAll(".accepted-answer-field")];
    const selected = card.querySelector('input[type="radio"]:checked');
    return {
      prompt: card.querySelector(".question-prompt").value,
      type,
      subject: card.querySelector(".question-subject-field")?.value.trim() || "General",
      options: choiceFields.length ? choiceFields.map(input => input.value) : existing.options || [],
      correctOptionIndex: selected ? Number(selected.value) : existing.correctOptionIndex || 0,
      acceptedAnswers: acceptedFields.length ? acceptedFields.map(input => input.value) : existing.acceptedAnswers || [""],
      timeLimitSeconds: Number(card.querySelector(".question-footer .timer-select").value)
    };
  });
}

function syncBuilderSettingsFromForm() {
  const wheelToggle = document.getElementById("subject-wheel-toggle");
  if (wheelToggle) subjectWheelEnabled = wheelToggle.checked;
  const boardToggle = document.getElementById("custom-board-toggle");
  if (boardToggle) boardEnabled = boardToggle.checked;

  const tiles = [...document.querySelectorAll(".board-tile-editor")];
  if (tiles.length) {
    boardTiles = tiles.map(tile => ({
      action: tile.querySelector(".board-tile-action")?.value || "Start",
      amount: Number(tile.querySelector(".board-tile-amount")?.value || 0),
      subject: tile.querySelector(".board-tile-subject")?.value.trim() || "",
      prompt: tile.querySelector(".board-tile-prompt")?.value.trim() || "",
      label: tile.querySelector(".board-tile-label")?.value.trim() || "",
      imageDataUrl: boardTiles[Number(tile.dataset.tile)]?.imageDataUrl || ""
    }));
  }
  const boardTitleInput = document.getElementById("board-title");
  if (boardTitleInput) boardTitle = boardTitleInput.value.trim();
  boardLayout = document.getElementById("board-layout")?.value || boardLayout;
  boardStyle = document.getElementById("board-style")?.value || boardStyle;
}

function renderBuilder(quiz = null, mode = showMode) {
  stopPolling();
  const previousMode = showMode;
  showMode = quiz?.mode || mode || "Quiz";
  if (quiz) {
    builderDraft = {
      quizName: quiz.quizName,
      description: quiz.description,
      theme: quiz.theme
    };
  }
  const content = quiz || builderDraft || {};
  boardEnabled = quiz
    ? Boolean(quiz.boardEnabled || showMode === "Board")
    : showMode === "Board" || showMode === "Custom" && Boolean(builderDraft?.boardEnabled ?? !builderDraft);
  subjectWheelEnabled = quiz
    ? Boolean(quiz.subjectWheelEnabled)
    : showMode === "Board" ? false : Boolean(builderDraft?.subjectWheelEnabled ?? (showMode === "Custom" || subjectWheelEnabled));
  if (quiz?.boardTiles?.length) boardTiles = quiz.boardTiles.map(tile => ({ ...tile }));
  else if (builderDraft?.boardTiles?.length) boardTiles = builderDraft.boardTiles.map(tile => ({ ...tile }));
  else if (showMode !== previousMode || boardTiles.length === 0) boardTiles = defaultBoardTiles();
  boardTitle = quiz?.boardTitle || builderDraft?.boardTitle || "The Showdown";
  boardLayout = quiz?.boardLayout || builderDraft?.boardLayout || "Track";
  boardStyle = quiz?.boardStyle || builderDraft?.boardStyle || "Aurora";
  setTheme(content.theme || currentTheme);
  setNav("create");
  savedQuizId = quiz?.id || savedQuizId;
  currentTheme = content.theme || currentTheme;
  const localLibrary = usesBrowserQuizLibrary();
  const modeName = showModeOptions.find(([mode]) => mode === showMode)?.[1] || "Quiz game";
  const modeDescription = showMode === "Board"
    ? "Customize a board path, then add questions for its question tiles."
    : showMode === "Custom"
      ? "Compose an advanced show with a board, question rounds, and a subject wheel."
      : "Write questions and optionally add a subject wheel for the host.";
  const isCustomStudio = showMode === "Custom";
  const questionWorkbench = isCustomStudio
    ? `<section class="custom-question-workbench">
        <div class="custom-workbench-heading"><span class="card-kicker">ROUND DESIGN · ${questions.length} QUESTION${questions.length === 1 ? "" : "S"}</span><h2>Question bank</h2><p>Mix scored questions, surveys, and typed answers. Each round keeps its own subject and timer.</p></div>
        <div class="custom-question-mix">${renderCustomQuestionMix()}</div>
        <div id="question-list" class="question-list"></div>
      </section>`
    : `<div id="question-list" class="question-list"></div>`;
  app.innerHTML = `
    <a class="quiet-btn create-back-link" href="/create" data-app-link>← All show formats</a>
    <div class="content-heading ${isCustomStudio ? "custom-page-heading" : ""}">
      <p class="eyebrow"><span class="eyebrow-mark">✦</span> ${isCustomStudio ? "ADVANCED SHOW BUILDER" : escapeHtml(modeName)}</p>
      <h1>Build your ${escapeHtml(modeName.toLowerCase())}.</h1>
      <p>${escapeHtml(modeDescription)} Players join with one PIN.</p>
    </div>
    <section class="builder-layout ${isCustomStudio ? "builder-layout-custom" : ""}">
      <form class="builder-main ${isCustomStudio ? "custom-builder-main" : ""}" data-form="create">
        ${isCustomStudio ? `<div class="custom-identity-block"><span class="card-kicker">01 · SHOW IDENTITY</span><strong>Set the room tone</strong><small>These details appear before the first round and on the live scoreboard.</small></div>` : ""}
        <label class="field-label" for="quiz-name">${isCustomStudio ? "Show title" : "Give your quiz a name"}</label>
        <input id="quiz-name" class="text-field" name="quizName" maxlength="80" value="${escapeHtml(content.quizName || "")}" placeholder="e.g. The Great Movie Mix-Up" required>
        <label class="field-label" for="quiz-description" style="margin-top:17px">${isCustomStudio ? "Opening script" : "Add a short introduction"} <span style="color:var(--dim);font-weight:500">(optional)</span></label>
        <textarea id="quiz-description" class="text-field quiz-description" name="description" maxlength="280" placeholder="${isCustomStudio ? "Set the scene before the first module begins…" : "Set the scene before everyone plays…"}">${escapeHtml(content.description || "")}</textarea>
        <span class="field-label" style="margin-top:18px">${isCustomStudio ? "Production color system" : "Pick your quiz look"}</span>
        <div class="theme-options" role="radiogroup" aria-label="Quiz color theme">
          ${quizThemes.map(theme => `
            <label class="theme-choice" style="--swatch:${theme.color}">
              <input type="radio" name="theme" value="${theme.id}" ${theme.id === currentTheme ? "checked" : ""}>
              <span class="theme-swatch">${theme.name}</span>
            </label>`).join("")}
        </div>
        ${showMode === "Quiz"
          ? `${questionWorkbench}<div id="mode-settings"></div>`
          : `<div id="mode-settings"></div>${questionWorkbench}`}
        <div class="builder-actions">
          <button class="secondary-btn" type="button" data-action="add-question"><span aria-hidden="true">＋</span> Add a question</button>
          <span class="save-note">${savedQuizId ? "Editing a saved quiz" : localLibrary ? "Saves in this browser" : "Saves to your quiz library"}</span>
          <div style="display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end">
            <button class="secondary-btn" type="button" data-action="save-quiz">Save quiz</button>
            <button class="primary-btn" type="submit" data-intent="host">Host game show <span aria-hidden="true">→</span></button>
          </div>
        </div>
      </form>
      <aside class="builder-aside ${isCustomStudio ? "custom-producer-aside" : ""}">
        <span class="card-kicker">${isCustomStudio ? "SHOW PRODUCER" : "Quick tips"}</span>
        <h3>${isCustomStudio ? "Production monitor" : "Make it a moment."}</h3>
        <p>${isCustomStudio ? "An advanced show is a set of connected systems. Tune a module, then check the live preview before you host." : "A great quiz is easy to jump into and a little hard to forget."}</p>
        <div class="aside-divider"></div>
        ${isCustomStudio ? `<div class="custom-producer-meters"><div><strong>${boardEnabled ? "ONLINE" : "STANDBY"}</strong><span>Board module</span></div><div><strong>${subjectWheelEnabled ? "ONLINE" : "STANDBY"}</strong><span>Wheel module</span></div><div><strong>${questions.length}</strong><span>Question records</span></div></div>
          <div class="tip-row"><span class="tip-icon">▦</span><span>Board position and tile actions are resolved by the game server.</span></div>
          <div class="tip-row"><span class="tip-icon">◉</span><span>Wheel subjects are built from each question’s Subject field.</span></div>
          <div class="tip-row"><span class="tip-icon">✦</span><span>Survey responses are collected without adding points.</span></div>`
          : `<div class="tip-row"><span class="tip-icon">✓</span><span>Keep each question short and snappy.</span></div>
          <div class="tip-row"><span class="tip-icon">◷</span><span>Pick a timer that keeps the room buzzing.</span></div>
          <div class="tip-row"><span class="tip-icon">✦</span><span>Tap the circle beside the right answer.</span></div>
          <div class="tip-row"><span class="tip-icon">🎨</span><span>Choose a color palette and add a welcome note.</span></div>`}
        <div class="aside-divider"></div>
        <p>${localLibrary
          ? 'Your quizzes are saved on this device in this browser. They are not synced to other devices.'
          : 'Your quizzes are saved privately in <strong style="color:var(--lime)">My quizzes</strong>, so you can edit or host them later.'}</p>
      </aside>
    </section>`;
  drawQuestionCards();
  renderModeSettings();
}

function quizPayload(form) {
  syncQuestionsFromForm();
  syncBuilderSettingsFromForm();
  const data = new FormData(form);
  const payload = {
    quizName: data.get("quizName").toString().trim(),
    description: data.get("description").toString().trim(),
    theme: data.get("theme")?.toString() || currentTheme,
    mode: showMode,
    boardEnabled: showMode === "Board" || showMode === "Custom" && boardEnabled,
    subjectWheelEnabled: showMode !== "Board" && subjectWheelEnabled,
    boardTiles: showMode === "Board" || showMode === "Custom" && boardEnabled ? boardTiles : [],
    boardTitle,
    boardLayout,
    boardStyle,
    questions
  };
  builderDraft = payload;
  return payload;
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
        <a class="primary-btn" href="/create" data-page="create" data-app-link>Create a quiz <span aria-hidden="true">→</span></a>
      </div>
      <div class="library-empty">Create your first quiz and choose <strong>Save quiz</strong> to keep it here.</div>`;
    return;
  }
  app.innerHTML = `
    <div class="library-header content-heading">
      <div><p class="eyebrow"><span class="eyebrow-mark">✦</span> Your quiz studio</p><h1>Your quizzes.</h1><p>${localLibrary
        ? "Saved on this device in this browser. Use the same browser and website to find them again."
        : "Pick up where you left off. Edit, customize, or host any quiz in your library."}</p></div>
      <a class="primary-btn" href="/create" data-page="create" data-app-link>Create a quiz <span aria-hidden="true">→</span></a>
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
        const modeName = quiz.mode === "Board" ? "Digital board" : quiz.mode === "Custom" ? "Custom show" : "Quiz game";
        return `<article class="library-card">
          <div class="library-card-accent" style="--card-accent:${theme.color}"></div>
          <div class="library-card-content">
            <h2>${escapeHtml(quiz.quizName)}</h2>
            <p>${escapeHtml(quiz.description || "A little quiz, ready for game night.")}</p>
            <div class="library-card-meta"><span>${modeName} · ${quiz.questions.length} questions</span><span>${theme.name} theme</span></div>
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
      <label class="field-label question-subject-label" for="subject-${questionIndex}">Subject <span>Used by the wheel and question tiles</span>
        <input id="subject-${questionIndex}" class="option-field question-subject-field" maxlength="40" value="${escapeHtml(question.subject || "General")}" placeholder="General">
      </label>
      <label class="field-label question-type-label" for="question-type-${questionIndex}">Question type
        <select id="question-type-${questionIndex}" class="question-type-select timer-select" aria-label="Question type">
          <option value="MultipleChoice" ${(question.type || "MultipleChoice") === "MultipleChoice" ? "selected" : ""}>Multiple choice</option>
          <option value="Survey" ${question.type === "Survey" ? "selected" : ""}>Survey (no right or wrong answer)</option>
          <option value="TypeAnswer" ${question.type === "TypeAnswer" ? "selected" : ""}>Type the answer</option>
        </select>
      </label>
      ${question.type === "TypeAnswer" ? `
        <div class="answer-type-note">Players type a response. Add accepted spelling variations; matching ignores capitalization and surrounding spaces.</div>
        <div class="accepted-answers">
          ${(Array.isArray(question.acceptedAnswers) && question.acceptedAnswers.length ? question.acceptedAnswers : [""]).map((answer, answerIndex) => `
            <label class="accepted-answer-row">
              <span class="accepted-answer-label">${answerIndex === 0 ? "Accepted answer" : `Also accept ${answerIndex + 1}`}</span>
              <input class="option-field accepted-answer-field" maxlength="100" placeholder="Type an answer that should count as correct" value="${escapeHtml(answer)}" required>
              ${(Array.isArray(question.acceptedAnswers) ? question.acceptedAnswers.length : 0) > 1 ? `<button type="button" class="remove-answer" data-action="remove-accepted-answer" data-index="${questionIndex}" data-answer-index="${answerIndex}" aria-label="Remove accepted answer">×</button>` : ""}
            </label>`).join("")}
        </div>
        ${(Array.isArray(question.acceptedAnswers) ? question.acceptedAnswers.length : 0) < 10 ? `<button type="button" class="quiet-btn add-answer-btn" data-action="add-accepted-answer" data-index="${questionIndex}">＋ Add another accepted answer</button>` : ""}
      ` : `
        <div class="options-heading"><span>${question.type === "Survey" ? "Survey choices" : "Answer choices"}</span><span>${question.type === "Survey" ? "All responses are valid" : "Mark the correct one"}</span></div>
        ${question.type === "Survey" ? `<p class="survey-builder-note">No points are awarded. You’ll see each player’s choice and name after the timer ends.</p>` : ""}
        <div class="options-grid">
        ${question.options.map((option, optionIndex) => `
          <label class="option-row ${question.type === "Survey" ? "survey-choice-row" : ""}">
            ${question.type === "Survey" ? "" : `<input class="correct-radio" type="radio" name="correct-${questionIndex}" value="${optionIndex}" ${optionIndex === question.correctOptionIndex ? "checked" : ""} aria-label="Mark answer ${letters[optionIndex]} as correct">`}
            <input class="option-field answer-choice-field" maxlength="100" placeholder="${letters[optionIndex]}. Answer choice" value="${escapeHtml(option)}" required>
          </label>`).join("")}
        </div>
        ${question.options.length < 4 ? `<button type="button" class="quiet-btn add-answer-btn" data-action="add-option" data-index="${questionIndex}">＋ Add choice</button>` : ""}
      `}
      <div class="question-footer">
        <span class="question-type-caption">${question.type === "Survey" ? "Survey answers are gathered without scoring." : question.type === "TypeAnswer" ? "Correct answers earn points; matching ignores case." : "Correct answers earn points, with a speed bonus."}</span>
        <label class="field-label" style="display:flex;align-items:center;gap:8px;margin:0">Timer
          <select class="timer-select" aria-label="Question timer">
            ${[10, 15, 20, 30, 45, 60].map(seconds => `<option value="${seconds}" ${seconds === question.timeLimitSeconds ? "selected" : ""}>${seconds} sec</option>`).join("")}
          </select>
        </label>
      </div>
    </article>`).join("");
  const wheelPreview = document.querySelector(".wheel-preview-panel");
  if (wheelPreview) wheelPreview.outerHTML = renderSubjectWheelPreview();
  const questionCount = document.querySelector(".custom-workbench-heading .card-kicker");
  if (questionCount) questionCount.textContent = `ROUND DESIGN · ${questions.length} QUESTION${questions.length === 1 ? "" : "S"}`;
  const questionMix = document.querySelector(".custom-question-mix");
  if (questionMix) questionMix.innerHTML = renderCustomQuestionMix();
  const producerCount = document.querySelector(".custom-producer-meters div:last-child strong");
  if (producerCount) producerCount.textContent = String(questions.length);
  const moduleCount = document.querySelector(".custom-question-count");
  if (moduleCount) moduleCount.textContent = `✦ ${questions.length} question${questions.length === 1 ? "" : "s"}`;
}

function renderCustomQuestionMix() {
  const counts = {
    MultipleChoice: questions.filter(question => (question.type || "MultipleChoice") === "MultipleChoice").length,
    Survey: questions.filter(question => question.type === "Survey").length,
    TypeAnswer: questions.filter(question => question.type === "TypeAnswer").length
  };
  return `<span>${counts.MultipleChoice} scored choice</span><span>${counts.Survey} survey</span><span>${counts.TypeAnswer} typed answer</span>`;
}

function renderCustomPipeline() {
  const stages = [];
  if (boardEnabled) {
    stages.push(["Player turn", "Take the active seat and roll"]);
    stages.push(["Tile resolution", "Resolve the landed action"]);
  }
  if (subjectWheelEnabled)
    stages.push(["Subject wheel", "Host spins or chooses a subject"]);
  stages.push(["Question round", "Answer using each prompt’s scoring type"]);
  stages.push(["Score reveal", "Show points and update the leaderboard"]);
  const routingNote = boardEnabled
    ? "Question rounds are conditional on the board tile. The host can advance phases from the live control panel."
    : subjectWheelEnabled
      ? "The host selects a subject before each question round and can advance from the live control panel."
      : "Questions run in order; the host advances to each answer reveal and the next round.";
  return `<div class="custom-flow-panel">
    <div class="custom-flow-heading"><strong>Runtime pipeline</strong><span>SERVER-RESOLVED</span></div>
    <ol>${stages.map((stage, index) =>
      `<li><i>${String(index + 1).padStart(2, "0")}</i><span><strong>${stage[0]}</strong><small>${stage[1]}</small></span></li>`
    ).join("")}</ol>
    <p>${routingNote}</p>
  </div>`;
}

function renderModeSettings() {
  const container = document.getElementById("mode-settings");
  if (!container) return;
  const wheelControl = `
    <label class="mode-toggle">
      <input id="subject-wheel-toggle" type="checkbox" ${subjectWheelEnabled ? "checked" : ""}>
      <span><strong>Enable the subject wheel</strong><small>The host can spin randomly or choose a subject manually before each question.</small></span>
    </label>`;
  const boardToggle = showMode === "Custom" ? `
    <label class="mode-toggle">
      <input id="custom-board-toggle" type="checkbox" ${boardEnabled ? "checked" : ""}>
      <span><strong>Include a digital board</strong><small>Players take turns rolling and resolving your tile actions.</small></span>
    </label>` : "";
  const showBoard = showMode === "Board" || showMode === "Custom" && boardEnabled;
  const wheelPreview = showMode !== "Board" ? renderSubjectWheelPreview() : "";
  const customMode = showMode === "Custom";
  container.innerHTML = `
    <section class="mode-settings-panel ${customMode ? "custom-studio" : ""}">
      <div class="mode-settings-heading">
        <span class="card-kicker">${showMode === "Board" ? "BOARD GAME" : customMode ? "SHOW CONTROL ROOM" : "QUIZ GAME"}</span>
        <h3>${showMode === "Board" ? "Design your game board" : customMode ? "Compose your show" : "Quiz options"}</h3>
        <p>${showMode === "Board"
          ? "Build a visual track, then give every space its own action, label, and artwork."
          : customMode
            ? "An advanced production desk: combine board turns, question rounds, and a subject wheel in one show."
            : "Add a subject to each question to let the host spin or manually choose what comes next."}</p>
      </div>
      ${customMode ? `
        <div class="custom-studio-console">
          <div class="custom-studio-banner"><span class="custom-studio-glyph" aria-hidden="true">⌘</span><div><strong>Production modules</strong><small>Enable the mechanics you want to orchestrate.</small></div></div>
          <div class="mode-toggle-list">${boardToggle}${wheelControl}</div>
          <div class="custom-module-summary">
            <span class="${boardEnabled ? "module-ready" : ""}">▦ Board ${boardEnabled ? "armed" : "off"}</span>
            <span class="${subjectWheelEnabled ? "module-ready" : ""}">◉ Wheel ${subjectWheelEnabled ? "armed" : "off"}</span>
            <span class="custom-question-count">✦ ${questions.length} question${questions.length === 1 ? "" : "s"}</span>
          </div>
          ${renderCustomPipeline()}
        </div>` : showMode === "Quiz" ? `<div class="quiz-wheel-settings">${wheelControl}</div>` : ""}
      ${showMode === "Quiz" && subjectWheelEnabled || showMode === "Custom" && subjectWheelEnabled
        ? `<p class="mode-settings-note">Wheel subjects come from the Subject fields on your questions. Questions with the same subject share a wheel segment.</p>`
        : ""}
      ${wheelPreview}
      ${showBoard ? renderBoardEditor() : ""}
    </section>`;
}

function uniqueQuestionSubjects() {
  const subjects = [];
  const normalized = new Set();
  for (const question of questions) {
    const subject = question.subject?.trim() || "General";
    const key = subject.toLocaleLowerCase();
    if (!normalized.has(key)) {
      normalized.add(key);
      subjects.push(subject);
    }
  }
  return subjects;
}

function wheelGradient(subjects) {
  const count = Math.max(subjects.length, 1);
  return `conic-gradient(${subjects.map((_, index) =>
    `${wheelColors[index % wheelColors.length]} ${index * 100 / count}% ${(index + 1) * 100 / count}%`
  ).join(",") || "#a98bff 0 100%"})`;
}

function wheelRotationForSubject(subject, subjects) {
  const count = Math.max(subjects.length, 1);
  const index = Math.max(0, subjects.findIndex(candidate =>
    candidate.toLocaleLowerCase() === String(subject || "").toLocaleLowerCase()));
  return 1440 + 360 - (index + 0.5) * 360 / count;
}

function renderSubjectWheelPreview(subjects = uniqueQuestionSubjects(), wheelClass = "") {
  const safeSubjects = subjects.length ? subjects : ["Add a question subject"];
  return `<section class="wheel-preview-panel" aria-label="Subject wheel preview">
    <div class="wheel-preview-heading"><div><span class="card-kicker">LIVE PREVIEW</span><strong>Subject wheel</strong></div><span>${subjects.length} ${subjects.length === 1 ? "segment" : "segments"}</span></div>
    <div class="wheel-preview-stage">
      <span class="wheel-pointer" aria-hidden="true">▼</span>
      <div class="wheel-display ${wheelClass}" style="--wheel-gradient:${wheelGradient(safeSubjects)}" role="img" aria-label="Wheel with ${safeSubjects.map(escapeHtml).join(", ")} segments"><span>✦</span></div>
    </div>
    <div class="wheel-subjects">${safeSubjects.map((subject, index) =>
      `<span><i style="--subject-color:${wheelColors[index % wheelColors.length]}"></i>${escapeHtml(subject)}</span>`
    ).join("")}</div>
    ${subjectWheelEnabled ? "" : `<small class="wheel-preview-hint">Preview only · turn on the subject wheel to use it during the show.</small>`}
  </section>`;
}

function safeBoardImageSrc(value) {
  return /^data:image\/(?:png|jpeg|webp);base64,[A-Za-z0-9+/]+=*$/.test(value || "") ? value : "";
}

function boardTileDisplay(tile) {
  if (tile.label?.trim()) return tile.label.trim();
  const labels = {
    Start: "Start",
    Question: tile.subject ? `Question · ${tile.subject}` : "Question",
    Bonus: `+${tile.amount} points`,
    Penalty: `−${tile.amount} points`,
    MoveForward: `Forward ${tile.amount}`,
    MoveBackward: `Back ${tile.amount}`,
    SkipTurn: "Skip a turn",
    Custom: tile.prompt?.trim() || "Custom action"
  };
  return labels[tile.action] || tile.action;
}

function boardTileClass(action) {
  return `board-action-${String(action || "Question").toLowerCase()}`;
}

function boardGridPosition(index, tileCount, layout) {
  if (layout === "Track") {
    const side = Math.ceil((tileCount + 4) / 4);
    if (index < side)
      return { columns: side, rows: side, column: index + 1, row: side };
    if (index < side * 2 - 1)
      return { columns: side, rows: side, column: side, row: side * 2 - 1 - index };
    if (index < side * 3 - 2)
      return { columns: side, rows: side, column: side * 3 - 2 - index, row: 1 };
    return { columns: side, rows: side, column: 1, row: index - (side * 3 - 3) + 1 };
  }
  const columns = 6;
  const rowFromBottom = Math.floor(index / columns);
  return {
    columns,
    rows: Math.ceil(tileCount / columns),
    column: rowFromBottom % 2 === 0 ? index % columns + 1 : columns - index % columns,
    row: Math.ceil(tileCount / columns) - rowFromBottom
  };
}

function boardPreviewTile(tile, index, live = false, positions = []) {
  const image = safeBoardImageSrc(tile.imageDataUrl);
  const playersHere = live ? positions.filter(player => player.position === index) : [];
  return `<div class="board-visual-tile ${boardTileClass(tile.action)} ${index === 0 ? "board-visual-start" : ""}" ${live ? "" : `data-preview-tile="${index}"`}>
    <span class="board-visual-index">${index === 0 ? "START" : index + 1}</span>
    ${image ? `<img src="${image}" alt="" loading="lazy">` : ""}
    <strong>${escapeHtml(boardTileDisplay(tile))}</strong>
    ${playersHere.length ? `<span class="live-board-players">${playersHere.map(player =>
      `<i title="${escapeHtml(player.username)}">${escapeHtml(player.username.slice(0, 1).toUpperCase())}</i>`
    ).join("")}</span>` : ""}
  </div>`;
}

function renderBoardPreview() {
  const firstPosition = boardGridPosition(0, boardTiles.length, boardLayout);
  const tileMarkup = boardTiles.map((tile, index) => {
    const position = boardGridPosition(index, boardTiles.length, boardLayout);
    const placement = `grid-column:${position.column};grid-row:${position.row};`;
    return `<div style="${placement}">${boardPreviewTile(tile, index)}</div>`;
  }).join("");
  return `<div id="board-preview">
    <section class="board-visual board-style-${boardStyle.toLowerCase()} ${boardLayout === "SnakesAndLadders" ? "board-layout-snakes" : "board-layout-track"}" aria-label="${escapeHtml(boardTitle)} preview">
      <div class="board-visual-header"><span class="card-kicker">BOARD PREVIEW</span><strong>${escapeHtml(boardTitle)}</strong></div>
      <div class="board-visual-path ${boardLayout === "SnakesAndLadders" ? "board-layout-snakes-path" : "board-layout-track-path"}" style="--board-columns:${firstPosition.columns};--board-rows:${firstPosition.rows}">
        ${boardLayout === "Track" ? `<div class="board-track-center"><span aria-hidden="true">✦</span><strong>${escapeHtml(boardTitle)}</strong><small>FOLLOW THE PATH</small></div>` : ""}
        ${tileMarkup}
      </div>
    </section>
  </div>`;
}

function renderBoardEditor() {
  const actionOptions = [
    ["Question", "Question"],
    ["Bonus", "Bonus points"],
    ["Penalty", "Lose points"],
    ["MoveForward", "Move forward"],
    ["MoveBackward", "Move backward"],
    ["SkipTurn", "Skip next turn"],
    ["Custom", "Custom host prompt"]
  ];
  const tiles = boardTiles.map((tile, index) => {
    const usesAmount = ["Bonus", "Penalty", "MoveForward", "MoveBackward"].includes(tile.action);
    const amountLabel = tile.action === "MoveForward" || tile.action === "MoveBackward" ? "Spaces" : "Points";
    return `<article class="board-tile-editor" data-tile="${index}">
      <div class="board-tile-head"><span class="board-tile-number">${index === 0 ? "START" : `TILE ${index + 1}`}</span>
        ${index > 0 && boardTiles.length > 3 ? `<button type="button" class="remove-answer" data-action="remove-board-tile" data-index="${index}" aria-label="Remove tile ${index + 1}">×</button>` : ""}
      </div>
      ${index === 0
        ? `<div class="board-start-tile"><span aria-hidden="true">⌂</span><strong>Starting space</strong><small>Every player begins here.</small></div>`
        : `<label class="field-label">Tile action
            <select class="timer-select board-tile-action" aria-label="Tile ${index + 1} action">
              ${actionOptions.map(([value, label]) => `<option value="${value}" ${tile.action === value ? "selected" : ""}>${label}</option>`).join("")}
            </select>
          </label>
          ${tile.action === "Question" ? `<label class="field-label board-tile-extra">Question subject <span>Leave empty for any subject</span><input class="option-field board-tile-subject" maxlength="40" value="${escapeHtml(tile.subject || "")}" placeholder="Any subject"></label>` : ""}
          ${usesAmount ? `<label class="field-label board-tile-extra">${amountLabel}<input class="option-field board-tile-amount" type="number" min="1" max="1000" value="${Number(tile.amount) || 1}" required></label>` : ""}
          ${tile.action === "Custom" ? `<label class="field-label board-tile-extra">Host prompt<input class="option-field board-tile-prompt" maxlength="160" value="${escapeHtml(tile.prompt || "")}" placeholder="What should the player do?" required></label>` : ""}
        `}
      <label class="field-label board-tile-extra">Display label <span>Optional text shown on the board</span>
        <input class="option-field board-tile-label" maxlength="40" value="${escapeHtml(tile.label || "")}" placeholder="${escapeHtml(boardTileDisplay(tile))}">
      </label>
      <label class="field-label board-tile-extra">Tile artwork
        <input class="board-tile-image" type="file" accept="image/png,image/jpeg,image/webp" aria-label="Upload artwork for tile ${index + 1}">
      </label>
      ${safeBoardImageSrc(tile.imageDataUrl)
        ? `<div class="board-image-preview"><img src="${tile.imageDataUrl}" alt="Tile ${index + 1} artwork preview"><button type="button" class="quiet-btn" data-action="remove-board-image" data-index="${index}">Remove image</button></div>`
        : ""}
    </article>`;
  }).join("");
  return `<div class="board-editor">
    <div class="board-editor-intro"><strong>Board identity &amp; layout</strong><span>${boardTiles.length - 1} playable spaces · up to 40 tiles</span></div>
    <div class="board-design-controls">
      <label class="field-label">Board title<input id="board-title" class="option-field" maxlength="48" value="${escapeHtml(boardTitle)}" placeholder="The Showdown"></label>
      <label class="field-label">Path layout<select id="board-layout" class="timer-select">
        <option value="Track" ${boardLayout === "Track" ? "selected" : ""}>Around the board</option>
        <option value="SnakesAndLadders" ${boardLayout === "SnakesAndLadders" ? "selected" : ""}>Snakes &amp; ladders grid</option>
      </select></label>
      <label class="field-label">Board style<select id="board-style" class="timer-select">
        <option value="Aurora" ${boardStyle === "Aurora" ? "selected" : ""}>Aurora glow</option>
        <option value="Candy" ${boardStyle === "Candy" ? "selected" : ""}>Candy pop</option>
        <option value="Arcade" ${boardStyle === "Arcade" ? "selected" : ""}>Arcade neon</option>
      </select></label>
    </div>
    ${renderBoardPreview()}
    <div class="board-editor-intro board-tile-list-heading"><strong>Tile actions &amp; artwork</strong><span>Changes update the preview above</span></div>
    <div class="board-path">${tiles}</div>
    ${boardTiles.length < 40 ? `<button type="button" class="quiet-btn add-board-tile" data-action="add-board-tile">＋ Add a tile</button>` : ""}
    <p class="mode-settings-note">Images are resized and compressed before saving. Each image must stay under 60 KB; all board artwork together is limited to 400 KB.</p>
  </div>`;
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

function gameWinners(players) {
  if (!players.length || players[0].score <= 0) return [];
  const winningScore = players[0].score;
  return players.filter(player => player.score === winningScore);
}

function renderWinnerAnnouncement(players, username = null, animate = false, winnerUsername = null) {
  const winners = winnerUsername
    ? players.filter(player => player.username.toLocaleLowerCase() === winnerUsername.toLocaleLowerCase())
    : gameWinners(players);
  if (!players.length) return "";
  if (!winners.length) {
    return `<section class="winner-stage ${animate ? "winner-stage-enter" : ""}" aria-live="polite">
      <div class="winner-crown" aria-hidden="true">✦</div>
      <p class="winner-kicker">Thanks for playing!</p>
      <h2 class="winner-names">What a great game</h2>
      <p class="winner-message">${username ? `${escapeHtml(username)}, thanks for taking part.` : "No points this time — there’s always the next round."}</p>
    </section>`;
  }
  const isTie = winners.length > 1;
  const playerWon = username !== null &&
    winners.some(winner => winner.username.toLocaleLowerCase() === username.toLocaleLowerCase());
  const headline = isTie ? "It's a tie!" : "And the winner is…";
  const winnerNames = winners.map(winner => escapeHtml(winner.username)).join(" &amp; ");
  const personalMessage = username === null
    ? "What a game. Thanks for playing!"
    : playerWon
      ? "You took the top spot. Fantastic playing!"
      : `You scored ${players.find(player => player.username.toLocaleLowerCase() === username.toLocaleLowerCase())?.score.toLocaleString() || 0} points. Great game!`;
  return `
    <section class="winner-stage ${animate ? "winner-stage-enter" : ""}" aria-live="polite">
      ${animate ? `<div class="winner-confetti" aria-hidden="true">${Array.from({ length: 14 }, () => "<i></i>").join("")}</div>` : ""}
      <div class="winner-crown" aria-hidden="true">${isTie ? "✦" : "♛"}</div>
      <p class="winner-kicker">${escapeHtml(headline)}</p>
      <h2 class="winner-names">${winnerNames}</h2>
      <strong class="winner-score">${winners[0].score.toLocaleString()} <span>POINTS</span></strong>
      <p class="winner-message">${escapeHtml(personalMessage)}</p>
    </section>`;
}

function renderLeaderboard(players, showRoundPoints = false, animateScores = false) {
  if (!players.length)
    return `<div class="empty-state">Your players will show up here.</div>`;

  let previousScore = null;
  let previousRank = 0;
  const rows = players.map((player, index) => {
    const rank = player.score === previousScore ? previousRank : index + 1;
    previousScore = player.score;
    previousRank = rank;
    const roundPoints = showRoundPoints && player.lastAnswerPoints !== null
      ? `<span class="round-points">${player.lastAnswerPoints > 0 ? "+" : ""}${player.lastAnswerPoints.toLocaleString()} this round</span>`
      : "";
    return `<li class="score-row ${rank === 1 ? "score-row-leader" : ""} ${animateScores && player.lastAnswerPoints !== null ? "score-row-update" : ""}">
      <span class="score-player"><span class="score-rank">${rank === 1 ? "★" : rank}</span><span class="score-name">${escapeHtml(player.username)}</span>${roundPoints}</span>
      <strong>${player.score.toLocaleString()} <span>pts</span></strong>
    </li>`;
  }).join("");
  return `<ol class="score-list" aria-label="Leaderboard">${rows}</ol>`;
}

function renderRoundSummary(players, animate = false) {
  const answered = players.filter(player => player.lastAnswerPoints !== null).length;
  const correct = players.filter(player => player.lastAnswerPoints > 0).length;
  const points = players.reduce((total, player) => total + (player.lastAnswerPoints || 0), 0);
  return `<div class="round-summary ${animate ? "round-summary-enter" : ""}" aria-live="polite">
    <div><strong>${correct}</strong><span>correct</span></div>
    <div><strong>${answered}/${players.length}</strong><span>answered</span></div>
    <div><strong>+${points.toLocaleString()}</strong><span>points this round</span></div>
  </div><p class="scoring-note">Correct answers earn 500 points, plus up to 500 bonus points for speed.</p>`;
}

function renderSurveyResults(question, responses) {
  const answers = responses || [];
  return `<section class="survey-results" aria-live="polite">
    <h3>Survey results <span>${answers.length} ${answers.length === 1 ? "response" : "responses"}</span></h3>
    ${question.options.map((option, index) => {
      const selectedBy = answers.filter(response => response.optionIndex === index);
      return `<div class="survey-result-row">
        <div class="survey-result-heading"><strong>${escapeHtml(option)}</strong><span>${selectedBy.length}</span></div>
        <div class="survey-result-bar"><span style="width:${answers.length ? selectedBy.length / answers.length * 100 : 0}%"></span></div>
        <p>${selectedBy.length ? selectedBy.map(response => escapeHtml(response.username)).join(", ") : "No responses yet"}</p>
      </div>`;
    }).join("")}
  </section>`;
}

function snapshotTransition(previous, snapshot, modeType) {
  if (!previous) return null;
  if (snapshot.status === "Finished" && previous.status !== "Finished")
    return { type: "finish" };
  if (JSON.stringify(snapshot.boardPositions) !== JSON.stringify(previous.boardPositions))
    return { type: "board-move" };
  if (snapshot.questionNumber !== previous.questionNumber)
    return { type: "question" };
  if (previous.status === "Question" && snapshot.status === "Reveal")
    return { type: "reveal" };
  if (modeType === "player" &&
      snapshot.status === "Question" &&
      previous.status === "Question" &&
      previous.myAnswerIndex === null &&
      snapshot.myAnswerIndex !== null)
    return { type: "answer" };
  return null;
}

function boardTileLabel(tile) {
  return boardTileDisplay(tile);
}

function renderBoardTrack(snapshot, animateMove = false) {
  const tiles = snapshot.boardTiles || [];
  const positions = snapshot.boardPositions || [];
  if (!tiles.length) return "";
  const layout = String(snapshot.boardLayout || "Track").toLowerCase() === "snakesandladders" ? "SnakesAndLadders" : "Track";
  const firstPosition = boardGridPosition(0, tiles.length, layout);
  const path = tiles.map((tile, index) => {
    const position = boardGridPosition(index, tiles.length, layout);
    const placement = `grid-column:${position.column};grid-row:${position.row};`;
    const playersHere = positions.filter(player => player.position === index);
    const currentTile = snapshot.activePlayer &&
      playersHere.some(player => player.username.toLocaleLowerCase() === snapshot.activePlayer.toLocaleLowerCase());
    const image = safeBoardImageSrc(tile.imageDataUrl);
    return `<div class="live-board-tile-wrap" style="${placement}">
      <div class="live-board-tile ${boardTileClass(tile.action)} ${index === 0 ? "live-board-start" : ""} ${currentTile ? "live-board-current" : ""}">
        <span class="live-board-index">${index === 0 ? "START" : index + 1 === tiles.length ? "FINISH" : index + 1}</span>
        ${image ? `<img src="${image}" alt="" loading="lazy">` : ""}
        <strong>${escapeHtml(boardTileLabel(tile))}</strong>
        <span class="live-board-players">${playersHere.map(player =>
          `<i title="${escapeHtml(player.username)}">${escapeHtml(player.username.slice(0, 1).toUpperCase())}</i>`
        ).join("")}</span>
      </div>
    </div>`;
  }).join("");
  return `<section class="live-board board-style-${String(snapshot.boardStyle || "Aurora").toLowerCase()} ${animateMove ? "live-board-moving" : ""}" aria-label="${escapeHtml(snapshot.boardTitle || "Game board")}">
    <div class="live-board-heading"><strong>${escapeHtml(snapshot.boardTitle || "Game board")}</strong><span>${positions.length} ${positions.length === 1 ? "player" : "players"}</span></div>
    <div class="live-board-path ${layout === "SnakesAndLadders" ? "live-board-snakes" : "live-board-track"}" style="--board-columns:${firstPosition.columns};--board-rows:${firstPosition.rows}">
      ${layout === "Track" ? `<div class="board-track-center"><span aria-hidden="true">✦</span><strong>${escapeHtml(snapshot.boardTitle || "The Showdown")}</strong><small>FOLLOW THE PATH</small></div>` : ""}
      ${path}
    </div>
    ${snapshot.lastRoll ? `<p class="board-roll-note">Last roll: <strong>${snapshot.lastRoll}</strong></p>` : ""}
  </section>`;
}

function renderWheel(snapshot, isHost) {
  const subjects = snapshot.availableSubjects || [];
  if (!isHost) {
    return `<section class="wheel-waiting">
      <div class="wheel-preview-stage"><span class="wheel-pointer" aria-hidden="true">▼</span><div class="wheel-display" style="--wheel-gradient:${wheelGradient(subjects)}" aria-hidden="true"><span>✦</span></div></div>
      <p class="card-kicker">${snapshot.boardEnabled ? "QUESTION TILE" : "SUBJECT WHEEL"}</p>
      <h2>${snapshot.boardEnabled ? "A subject is being chosen" : "The next subject is up to chance."}</h2>
      <p>${escapeHtml(snapshot.tileMessage || "Your host can spin the wheel or pick a subject.")}</p>
      ${subjects.length ? `<div class="wheel-subjects">${subjects.map(subject => `<span>${escapeHtml(subject)}</span>`).join("")}</div>` : ""}
    </section>`;
  }
  return `<section class="wheel-host-panel">
    <div class="wheel-preview-stage"><span class="wheel-pointer" aria-hidden="true">▼</span><div class="wheel-display" style="--wheel-gradient:${wheelGradient(subjects)}" aria-hidden="true"><span>✦</span></div></div>
    <p class="card-kicker">${snapshot.boardEnabled ? "BOARD QUESTION" : "SUBJECT WHEEL"}</p>
    <h2>Choose what comes next.</h2>
    <p id="wheel-result-announcement" aria-live="polite">${escapeHtml(pendingWheelResult ? `The wheel is landing on ${pendingWheelResult}…` : snapshot.tileMessage || "Spin randomly, or choose one of the available subjects.")}</p>
    <div class="wheel-subjects">${subjects.map(subject => `<span>${escapeHtml(subject)}</span>`).join("")}</div>
    <label class="field-label" for="wheel-subject">Or choose a subject</label>
    <select id="wheel-subject" class="timer-select wheel-select" ${subjects.length ? "" : "disabled"}>
      ${subjects.map(subject => `<option value="${escapeHtml(subject)}">${escapeHtml(subject)}</option>`).join("")}
    </select>
    <div class="wheel-actions">
      <button class="secondary-btn" data-action="spin-wheel" ${subjects.length ? "" : "disabled"}>Spin randomly</button>
      <button class="primary-btn" data-action="choose-wheel-subject" ${subjects.length ? "" : "disabled"}>Choose subject <span aria-hidden="true">→</span></button>
    </div>
  </section>`;
}

function renderHost(snapshot, transition) {
  const host = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
  if (!host) return renderJoin();
  setTheme(snapshot.theme);
  setNav("");
  const lobby = snapshot.status === "Lobby";
  const finished = snapshot.status === "Finished";
  const wheel = snapshot.status === "Wheel";
  const boardTurn = snapshot.boardEnabled && snapshot.status === "BoardTurn";
  const boardAction = snapshot.boardEnabled && snapshot.status === "BoardAction";
  const title = finished ? "That’s a wrap!" : snapshot.quizName;
  const players = snapshot.players || [];
  const scoreList = renderLeaderboard(
    players,
    snapshot.status === "Reveal" || finished,
    transition?.type === "reveal"
  );
  const questionType = snapshot.currentQuestion?.type || "MultipleChoice";
  const isPublicHost = usesBrowserQuizLibrary();
  app.innerHTML = `
    <section class="game-layout">
      <div class="game-header"><h1>${escapeHtml(title)}</h1><div class="game-pin"><span>JOIN PIN</span><strong>${escapeHtml(snapshot.pin)}</strong></div></div>
      ${lobby ? `
        <div class="lobby-layout">
          <section class="waiting-panel">
            <div class="game-meta"><span>THE LOBBY</span><strong>${snapshot.playerCount} ${snapshot.playerCount === 1 ? "player" : "players"}</strong></div>
            <h2 style="margin-top:22px">The room is open.</h2>
            <p>${escapeHtml(snapshot.description || "Share the PIN with your crew. When everyone’s in, start the show.")}</p>
            <div class="player-roster">${players.length ? players.map(player => `<div class="roster-item"><span class="roster-avatar">${escapeHtml(player.username.slice(0, 1).toUpperCase())}</span><span>${escapeHtml(player.username)}</span></div>`).join("") : `<div class="empty-state">Waiting for the first player to join…</div>`}</div>
          </section>
          <aside class="host-actions">
            <div class="waiting-panel room-code"><span class="code-label">Your game PIN</span><strong class="big-code">${escapeHtml(snapshot.pin)}</strong><span class="copy-note">Share this code so players can join.</span>${isPublicHost ? `<button class="secondary-btn" data-action="copy-invite" style="margin-top:13px;width:100%">Copy player website</button>` : ""}</div>
            <button class="primary-btn" data-action="start" ${snapshot.playerCount === 0 ? "disabled" : ""}>${snapshot.boardEnabled ? "Start the board game" : "Start the show"} <span aria-hidden="true">→</span></button>
            <button class="quiet-btn" data-action="leave">← Back to Paradox</button>
          </aside>
        </div>
      ` : finished ? `
        <div class="game-panel">
          ${snapshot.boardEnabled ? renderBoardTrack(snapshot, transition?.type === "board-move") : ""}
          ${renderWinnerAnnouncement(players, null, transition?.type === "finish", snapshot.winnerUsername)}
          <h2 class="leaderboard-heading">Final leaderboard</h2>
          ${scoreList}
          <div style="display:flex;justify-content:center;margin-top:22px"><button class="primary-btn" data-action="new-quiz">Create another quiz</button></div>
        </div>
      ` : wheel ? `
        <div class="game-panel">
          ${snapshot.boardEnabled ? renderBoardTrack(snapshot, transition?.type === "board-move") : ""}
          ${renderWheel(snapshot, true)}
          ${snapshot.boardEnabled ? `<p class="board-active-player">Current player: <strong>${escapeHtml(snapshot.activePlayer || "")}</strong></p>` : ""}
          <div class="aside-divider"></div><h3 class="leaderboard-heading">Live leaderboard</h3>${scoreList}
        </div>
      ` : boardTurn || boardAction ? `
        <div class="game-panel">
          ${renderBoardTrack(snapshot, transition?.type === "board-move")}
          <section class="board-turn-panel ${boardAction ? "board-custom-action" : ""}">
            <p class="card-kicker">${boardAction ? "CUSTOM TILE" : "BOARD TURN"}</p>
            <h2>${boardAction ? "A host prompt is waiting" : `It’s ${escapeHtml(snapshot.activePlayer || "the next player")}’s turn`}</h2>
            <p>${escapeHtml(snapshot.tileMessage || (boardAction ? "Review the custom tile action." : "The active player rolls from their own device."))}</p>
            ${boardAction ? `<button class="primary-btn" data-action="next">${snapshot.winnerUsername ? "Finish game" : "Continue turn"} <span aria-hidden="true">→</span></button>` : `<span class="board-waiting-note">Waiting for the active player to roll…</span>`}
          </section>
          <h3 class="leaderboard-heading">Leaderboard</h3>${scoreList}
        </div>
      ` : `
        <div class="game-panel">
          ${snapshot.boardEnabled ? renderBoardTrack(snapshot, transition?.type === "board-move") : ""}
          ${snapshot.boardEnabled && snapshot.tileMessage ? `<p class="board-active-player">${escapeHtml(snapshot.tileMessage)}</p>` : ""}
          <div class="game-meta"><span>${snapshot.status === "Reveal" ? "ANSWER REVEAL" : "QUESTION " + snapshot.questionNumber + " OF " + snapshot.questionCount}</span><strong class="question-clock">${snapshot.status === "Question" ? secondsLeft(snapshot.endsAtUtc) + " sec" : snapshot.playerCount + " playing"}</strong></div>
          <div class="timer-track"><div class="timer-fill" style="width:${timerPercent(snapshot)}%"></div></div>
          ${snapshot.activeSubject ? `<div class="question-subject-pill">✦ ${escapeHtml(snapshot.activeSubject)}</div>` : ""}
          <h2 class="game-question ${transition?.type === "question" ? "question-enter" : ""}">${escapeHtml(snapshot.currentQuestion?.prompt)}</h2>
          ${questionType === "TypeAnswer"
            ? `<div class="host-question-note">Players type their answer. Accepted spellings count as correct.</div>`
            : `<div class="answer-grid">${(snapshot.currentQuestion?.options || []).map((option, index) => `<div class="answer-card ${snapshot.status === "Reveal" && questionType === "MultipleChoice" ? (index === snapshot.correctOptionIndex ? "is-correct" : "is-wrong") : ""}"><span class="answer-symbol">${letters[index]}</span><span>${escapeHtml(option)}</span></div>`).join("")}</div>`}
          ${snapshot.status === "Reveal"
            ? questionType === "Survey"
              ? renderSurveyResults(snapshot.currentQuestion, snapshot.surveyResponses)
              : `${renderRoundSummary(players, transition?.type === "reveal")}${questionType === "MultipleChoice"
                ? `<div class="reveal-banner">Correct answer: <strong>${escapeHtml(snapshot.currentQuestion?.options[snapshot.correctOptionIndex] || "")}</strong></div>`
                : `<div class="reveal-banner">Accepted answers include: <strong>${escapeHtml(snapshot.currentQuestion?.acceptedAnswers?.join(", ") || "the answer key")}</strong></div>`}`
            : `<p class="answer-status">${players.filter(player => player.lastAnswerPoints !== null).length} of ${snapshot.playerCount} answered${questionType === "Survey" ? " · survey answers are not scored" : ""}</p>`}
          ${snapshot.status === "Reveal" ? `<div style="display:flex;justify-content:center;margin:20px 0 27px"><button class="primary-btn" data-action="next">${snapshot.boardEnabled ? "Continue turn" : snapshot.questionNumber >= snapshot.questionCount ? "See final scores" : "Next question"} <span aria-hidden="true">→</span></button></div>` : ""}
          <div class="aside-divider"></div><h3 class="leaderboard-heading">${snapshot.status === "Reveal" ? "Updated leaderboard" : "Live leaderboard"}</h3>${scoreList}
        </div>
      `}
    </section>`;
}

function renderPlayer(snapshot, transition) {
  const player = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
  if (!player) return renderJoin();
  setTheme(snapshot.theme);
  setNav("");
  const lobby = snapshot.status === "Lobby";
  const finished = snapshot.status === "Finished";
  const wheel = snapshot.status === "Wheel";
  const boardTurn = snapshot.boardEnabled && snapshot.status === "BoardTurn";
  const boardAction = snapshot.boardEnabled && snapshot.status === "BoardAction";
  const players = snapshot.players || [];
  const playerScore = players.find(entry => entry.username.toLocaleLowerCase() === player.username.toLocaleLowerCase());
  const isActiveBoardPlayer = !snapshot.boardEnabled ||
    snapshot.activePlayer?.toLocaleLowerCase() === player.username.toLocaleLowerCase();
  const questionType = snapshot.currentQuestion?.type || "MultipleChoice";
  const hasSubmittedAnswer = snapshot.myAnswerIndex !== null || snapshot.myAnswerText !== null;
  const scoreList = renderLeaderboard(
    players,
    snapshot.status === "Reveal" || finished,
    transition?.type === "reveal"
  );
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
          ${snapshot.boardEnabled ? renderBoardTrack(snapshot, transition?.type === "board-move") : ""}
          ${renderWinnerAnnouncement(players, player.username, transition?.type === "finish", snapshot.winnerUsername)}
          <h2 class="leaderboard-heading">Final leaderboard</h2>${scoreList}
          <div style="display:flex;justify-content:center;margin-top:24px"><button class="secondary-btn" data-action="leave">Back to Paradox</button></div>
        </div>
      ` : wheel ? `
        <div class="game-panel">
          ${snapshot.boardEnabled ? renderBoardTrack(snapshot, transition?.type === "board-move") : ""}
          ${renderWheel(snapshot, false)}
          ${snapshot.boardEnabled ? `<p class="board-active-player">Current player: <strong>${escapeHtml(snapshot.activePlayer || "")}</strong></p>` : ""}
        </div>
      ` : boardTurn ? `
        <div class="game-panel">
          ${renderBoardTrack(snapshot, transition?.type === "board-move")}
          <section class="board-turn-panel">
            <p class="card-kicker">BOARD TURN</p>
            ${isActiveBoardPlayer
              ? `<h2>Your turn, ${escapeHtml(player.username)}!</h2><p>${escapeHtml(snapshot.tileMessage || "Roll the die. Paradox will move your piece automatically.")}</p><button class="primary-btn" data-action="roll-board">Roll the die <span aria-hidden="true">→</span></button>`
              : `<h2>It’s ${escapeHtml(snapshot.activePlayer || "another player")}’s turn.</h2><p>${escapeHtml(snapshot.tileMessage || "Watch the board while they roll.")}</p><span class="board-waiting-note">Your turn is coming up.</span>`}
          </section>
          <h3 class="leaderboard-heading">Leaderboard</h3>${scoreList}
        </div>
      ` : boardAction ? `
        <div class="game-panel">
          ${renderBoardTrack(snapshot, transition?.type === "board-move")}
          <section class="board-turn-panel board-custom-action">
            <p class="card-kicker">CUSTOM TILE</p>
            <h2>${isActiveBoardPlayer ? "Your host has a prompt for you" : `${escapeHtml(snapshot.activePlayer || "A player")} landed on a custom tile`}</h2>
            <p>${escapeHtml(snapshot.tileMessage || "Wait for the host to continue.")}</p>
            <span class="board-waiting-note">${isActiveBoardPlayer ? "Follow the prompt while your host reviews the turn." : "The host will continue the game shortly."}</span>
          </section>
          <h3 class="leaderboard-heading">Leaderboard</h3>${scoreList}
        </div>
      ` : `
        <div class="game-panel">
          ${snapshot.boardEnabled ? renderBoardTrack(snapshot, transition?.type === "board-move") : ""}
          ${snapshot.boardEnabled && snapshot.tileMessage ? `<p class="board-active-player">${escapeHtml(snapshot.tileMessage)}</p>` : ""}
          ${snapshot.boardEnabled && snapshot.status === "Question" && !isActiveBoardPlayer
            ? `<section class="board-turn-panel"><p class="card-kicker">BOARD QUESTION</p><h2>${escapeHtml(snapshot.activePlayer || "The active player")} is answering</h2><p>Only the player whose turn it is can answer this question.</p></section>`
            : `
          <div class="game-meta"><span>${snapshot.status === "Reveal" ? "ANSWER REVEAL" : "QUESTION " + snapshot.questionNumber + " OF " + snapshot.questionCount}</span><strong class="question-clock">${snapshot.status === "Question" ? secondsLeft(snapshot.endsAtUtc) + " sec" : "SCORES"}</strong></div>
          <div class="timer-track"><div class="timer-fill" style="width:${timerPercent(snapshot)}%"></div></div>
          ${snapshot.activeSubject ? `<div class="question-subject-pill">✦ ${escapeHtml(snapshot.activeSubject)}</div>` : ""}
          <h2 class="game-question ${transition?.type === "question" ? "question-enter" : ""}">${escapeHtml(snapshot.currentQuestion?.prompt)}</h2>
          ${questionType === "TypeAnswer"
            ? snapshot.status === "Reveal"
              ? `<div class="typed-answer-display"><span>Your answer</span><strong>${escapeHtml(snapshot.myAnswerText || "No answer submitted")}</strong></div>`
              : `<form class="type-answer-form" data-form="answer-text">
                  <label class="field-label" for="typed-answer">Type your answer</label>
                  <div class="type-answer-controls">
                    <input id="typed-answer" class="text-field" name="answerText" maxlength="100" placeholder="Your answer…" value="${escapeHtml(snapshot.myAnswerText || "")}" ${hasSubmittedAnswer ? "disabled" : ""} required>
                    <button class="primary-btn" type="submit" ${hasSubmittedAnswer ? "disabled" : ""}>${hasSubmittedAnswer ? "Submitted" : "Submit answer"} <span aria-hidden="true">→</span></button>
                  </div>
                </form>`
            : `<div class="answer-grid">${(snapshot.currentQuestion?.options || []).map((option, index) => {
              const selected = snapshot.myAnswerIndex === index;
              const correct = snapshot.status === "Reveal" && questionType === "MultipleChoice" && index === snapshot.correctOptionIndex;
              const wrong = snapshot.status === "Reveal" && questionType === "MultipleChoice" && selected && !correct;
              return `<button class="answer-card ${selected ? "selected" : ""} ${selected && transition?.type === "answer" ? "answer-submit-enter" : ""} ${correct ? "is-correct" : ""} ${wrong ? "is-wrong" : ""}" data-action="answer" data-index="${index}" ${snapshot.status !== "Question" || hasSubmittedAnswer ? "disabled" : ""}><span class="answer-symbol">${correct ? "✓" : wrong ? "×" : letters[index]}</span><span>${escapeHtml(option)}</span>${selected && snapshot.status === "Question" ? `<span class="answer-locked-label">LOCKED IN</span>` : ""}</button>`;
            }).join("")}</div>`}
          ${snapshot.status === "Reveal" && questionType === "Survey"
            ? `<div class="survey-player-result" role="status"><strong>${snapshot.myAnswerIndex !== null ? "Thanks for sharing!" : "Time’s up!"}</strong><span>${snapshot.myAnswerIndex !== null ? `Your response was recorded: ${escapeHtml(snapshot.currentQuestion?.options[snapshot.myAnswerIndex] || "")}.` : "No response was recorded before the timer ended."} Survey answers don't affect scores.</span></div>`
            : snapshot.status === "Reveal"
            ? `<div class="answer-result ${snapshot.myAnswerIndex === snapshot.correctOptionIndex ? "answer-result-correct" : "answer-result-wrong"} ${transition?.type === "reveal" ? "answer-result-enter" : ""}" role="status">
                <span class="answer-result-icon" aria-hidden="true">${questionType === "MultipleChoice" ? snapshot.myAnswerIndex === snapshot.correctOptionIndex ? "✓" : snapshot.myAnswerIndex === null ? "…" : "×" : (playerScore?.lastAnswerPoints || 0) > 0 ? "✓" : snapshot.myAnswerText === null ? "…" : "×"}</span>
                <span class="answer-result-copy"><strong>${questionType === "MultipleChoice"
                  ? snapshot.myAnswerIndex === null ? "Time's up!" : snapshot.myAnswerIndex === snapshot.correctOptionIndex ? "Brilliant answer!" : "Not this time"
                  : snapshot.myAnswerText === null ? "Time's up!" : (playerScore?.lastAnswerPoints || 0) > 0 ? "Answer accepted!" : "Not quite"}</strong><span>${questionType === "MultipleChoice"
                  ? snapshot.myAnswerIndex === null ? "The answer was revealed." : snapshot.myAnswerIndex === snapshot.correctOptionIndex ? "Fast and correct — points added to your score." : `Correct answer: ${escapeHtml(snapshot.currentQuestion?.options[snapshot.correctOptionIndex] || "")}`
                  : snapshot.myAnswerText === null ? "The accepted answers are shown above." : (playerScore?.lastAnswerPoints || 0) > 0 ? "That matches an accepted answer. Points added to your score." : "Check the accepted answers and try again next round."}</span></span>
                <strong class="answer-result-points">${(playerScore?.lastAnswerPoints || 0) > 0 ? `+${playerScore.lastAnswerPoints}` : "+0"}<span>PTS</span></strong>
              </div>`
            : hasSubmittedAnswer
              ? `<div class="answer-submitted ${transition?.type === "answer" ? "answer-submitted-enter" : ""}" role="status"><span class="answer-submitted-icon" aria-hidden="true">✓</span><span><strong>${questionType === "Survey" ? "Response recorded!" : "Answer locked in!"}</strong><small>${questionType === "Survey" ? "Thanks! Your response will be included in the survey results." : "Nice and quick. Your result is revealed when the timer ends."}</small></span><span class="answer-submitted-dots" aria-hidden="true"><i></i><i></i><i></i></span></div>`
              : `<p class="answer-status">Choose your answer before the timer runs out.</p>`}
          ${snapshot.status === "Reveal" && questionType !== "Survey" ? renderRoundSummary(players, transition?.type === "reveal") : ""}
          ${snapshot.status === "Reveal" ? `<h3 class="leaderboard-heading">Leaderboard</h3>${scoreList}` : ""}
          `}
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
    snapshot.myAnswerIndex,
    snapshot.myAnswerText,
    snapshot.surveyResponses,
    snapshot.availableSubjects,
    snapshot.activeSubject,
    snapshot.activePlayer,
    snapshot.lastRoll,
    snapshot.boardPositions,
    snapshot.boardTitle,
    snapshot.boardLayout,
    snapshot.boardStyle,
    snapshot.boardTiles,
    snapshot.tileMessage,
    snapshot.winnerUsername
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
        if (wheelAnimationUntil > Date.now() && snapshot.status !== "Wheel") {
          updateClock(snapshot);
          return;
        }
        const signature = snapshotSignature(snapshot);
        if (signature !== lastSnapshotSignature) {
          const transition = snapshotTransition(previousRoomSnapshot, snapshot, "host");
          previousRoomSnapshot = snapshot;
          lastSnapshotSignature = signature;
          renderHost(snapshot, transition);
        }
        updateClock(snapshot);
      }
    } else {
      const snapshot = await api(`/api/games/${encodeURIComponent(mode.room.pin)}`, { headers: { "X-Player-Id": mode.room.playerId } });
      if (currentMode === mode) {
        const signature = snapshotSignature(snapshot);
        if (signature !== lastSnapshotSignature) {
          const transition = snapshotTransition(previousRoomSnapshot, snapshot, "player");
          previousRoomSnapshot = snapshot;
          lastSnapshotSignature = signature;
          renderPlayer(snapshot, transition);
        }
        updateClock(snapshot);
      }
    }
  } catch (error) {
    if (currentMode === mode) {
      stopPolling();
      sessionStorage.removeItem(mode.type === "host" ? "paradox-host" : "paradox-player");
      navigateTo("/join");
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
  const route = routeFor();
  const host = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
  const player = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
  if (route.page === "host" && host?.pin === route.pin) return startPolling("host", host);
  if (route.page === "play" && player?.pin === route.pin) return startPolling("player", player);
  renderRoute();
}

window.addEventListener("popstate", renderRoute);

document.addEventListener("click", async event => {
  const appLink = event.target.closest("a[data-app-link]");
  if (appLink &&
      event.button === 0 &&
      !event.metaKey &&
      !event.ctrlKey &&
      !event.shiftKey &&
      !event.altKey &&
      (!appLink.target || appLink.target === "_self")) {
    event.preventDefault();
    if (routeFor().page === "builder") {
      const form = document.querySelector('form[data-form="create"]');
      if (form) quizPayload(form);
    }
    if (appLink.dataset.page === "create") resetBuilder();
    navigateTo(appLink.pathname);
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
        const mode = quiz.mode || "Quiz";
        setPath(modePath(mode));
        renderBuilder(quiz, mode);
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
  if (action === "add-board-tile") {
    syncQuestionsFromForm();
    syncBuilderSettingsFromForm();
    if (boardTiles.length >= 40) return notify("A board can have up to 40 tiles.");
    boardTiles.push({ action: "Question", amount: 100, subject: "", prompt: "" });
    renderModeSettings();
    return;
  }
  if (action === "remove-board-tile") {
    syncQuestionsFromForm();
    syncBuilderSettingsFromForm();
    if (boardTiles.length <= 3) return notify("A board needs at least 3 tiles.");
    boardTiles.splice(Number(actionButton.dataset.index), 1);
    renderModeSettings();
    return;
  }
  if (action === "remove-board-image") {
    syncQuestionsFromForm();
    syncBuilderSettingsFromForm();
    boardTiles[Number(actionButton.dataset.index)].imageDataUrl = "";
    renderModeSettings();
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
  if (action === "add-accepted-answer") {
    syncQuestionsFromForm();
    const question = questions[Number(actionButton.dataset.index)];
    if (question.acceptedAnswers.length < 10) question.acceptedAnswers.push("");
    drawQuestionCards();
    document.querySelector(`[data-question="${actionButton.dataset.index}"] .accepted-answer-row:last-child .accepted-answer-field`)?.focus();
    return;
  }
  if (action === "remove-accepted-answer") {
    syncQuestionsFromForm();
    const question = questions[Number(actionButton.dataset.index)];
    question.acceptedAnswers.splice(Number(actionButton.dataset.answerIndex), 1);
    drawQuestionCards();
    return;
  }
  if (action === "back-to-join" || action === "leave") {
    sessionStorage.removeItem("paradox-player");
    sessionStorage.removeItem("paradox-host");
    navigateTo("/join");
    return;
  }
  if (action === "new-quiz") {
    sessionStorage.removeItem("paradox-host");
    resetBuilder();
    navigateTo("/create");
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
  if (action === "spin-wheel" || action === "choose-wheel-subject") {
    const room = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
    const randomSpin = action === "spin-wheel";
    const subject = !randomSpin
      ? document.getElementById("wheel-subject")?.value || null
      : null;
    actionButton.disabled = true;
    if (randomSpin) {
      wheelAnimationUntil = Date.now() + 3200;
      pendingWheelResult = "";
      document.querySelector(".wheel-display")?.classList.add("wheel-display-spin");
      const announcement = document.getElementById("wheel-result-announcement");
      if (announcement) announcement.textContent = "The wheel is spinning…";
    }
    try {
      const result = await post(`/api/games/${room.pin}/wheel/spin`, { subject }, { "X-Host-Key": room.hostKey });
      if (randomSpin) {
        pendingWheelResult = result.activeSubject || "";
        wheelAnimationUntil = Date.now() + 3200;
        const wheelDisplay = document.querySelector(".wheel-display");
        if (wheelDisplay) {
          wheelDisplay.classList.remove("wheel-display-spin");
          void wheelDisplay.offsetWidth;
          wheelDisplay.style.setProperty(
            "--wheel-spin-target",
            `${wheelRotationForSubject(pendingWheelResult, previousRoomSnapshot?.availableSubjects || [])}deg`);
          wheelDisplay.classList.add("wheel-display-spin");
        }
        const announcement = document.getElementById("wheel-result-announcement");
        if (announcement) announcement.textContent = pendingWheelResult
          ? `The wheel is landing on ${pendingWheelResult}…`
          : "The wheel has stopped.";
        setTimeout(() => {
          wheelAnimationUntil = 0;
          pendingWheelResult = "";
          refreshRoom();
        }, Math.max(0, wheelAnimationUntil - Date.now()));
      } else {
        await refreshRoom();
      }
    } catch (error) {
      wheelAnimationUntil = 0;
      pendingWheelResult = "";
      document.querySelectorAll(".wheel-display").forEach(display => {
        display.classList.remove("wheel-display-spin");
        display.style.removeProperty("--wheel-spin-target");
      });
      actionButton.disabled = false;
      notify(error.message);
    }
    return;
  }
  if (action === "roll-board") {
    const room = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
    actionButton.disabled = true;
    try {
      await post(`/api/games/${room.pin}/board/roll`, undefined, { "X-Player-Id": room.playerId });
      await refreshRoom();
    } catch (error) {
      actionButton.disabled = false;
      notify(error.message);
    }
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
  if (event.target.matches(".question-subject-field")) {
    syncQuestionsFromForm();
    const preview = document.querySelector(".wheel-preview-panel");
    if (preview) preview.outerHTML = renderSubjectWheelPreview();
  }
  if (event.target.matches("#board-title, .board-tile-label, .board-tile-subject, .board-tile-prompt, .board-tile-amount")) {
    syncBuilderSettingsFromForm();
    const preview = document.getElementById("board-preview");
    if (preview) preview.outerHTML = renderBoardPreview();
  }
});

async function compressBoardImage(file) {
  if (!file || !["image/png", "image/jpeg", "image/webp"].includes(file.type))
    throw new Error("Choose a PNG, JPEG, or WebP image.");
  if (file.size > 8 * 1024 * 1024)
    throw new Error("Choose an image smaller than 8 MB.");
  const bitmap = await createImageBitmap(file);
  try {
    const scale = Math.min(1, 420 / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(bitmap.width * scale));
    canvas.height = Math.max(1, Math.round(bitmap.height * scale));
    canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    for (const quality of [0.72, 0.58, 0.45]) {
      const dataUrl = canvas.toDataURL("image/webp", quality);
      if (dataUrl.length <= 80000) return dataUrl;
    }
    throw new Error("That image could not be compressed enough. Try a simpler picture.");
  } finally {
    bitmap.close();
  }
}

document.addEventListener("change", async event => {
  if (event.target.matches('input[name="theme"]')) {
    currentTheme = event.target.value;
    setTheme(currentTheme);
  }
  if (event.target.matches(".question-type-select")) {
    syncQuestionsFromForm();
    drawQuestionCards();
  }
  if (event.target.id === "subject-wheel-toggle") {
    subjectWheelEnabled = event.target.checked;
    syncBuilderSettingsFromForm();
    renderModeSettings();
  }
  if (event.target.id === "custom-board-toggle") {
    boardEnabled = event.target.checked;
    syncBuilderSettingsFromForm();
    renderModeSettings();
  }
  if (event.target.matches(".board-tile-action")) {
    syncQuestionsFromForm();
    syncBuilderSettingsFromForm();
    const tile = boardTiles[Number(event.target.closest(".board-tile-editor").dataset.tile)];
    if (["Bonus", "Penalty"].includes(tile.action) && !tile.amount) tile.amount = 100;
    if (["MoveForward", "MoveBackward"].includes(tile.action) && !tile.amount) tile.amount = 1;
    renderModeSettings();
  }
  if (event.target.matches("#board-layout, #board-style")) {
    syncBuilderSettingsFromForm();
    renderModeSettings();
  }
  if (event.target.matches(".board-tile-image")) {
    const tileIndex = Number(event.target.closest(".board-tile-editor").dataset.tile);
    const file = event.target.files?.[0];
    if (!file) return;
    try {
      const imageDataUrl = await compressBoardImage(file);
      syncBuilderSettingsFromForm();
      const currentImageLength = boardTiles[tileIndex].imageDataUrl?.length || 0;
      const totalImageLength = boardTiles.reduce((total, tile) => total + (tile.imageDataUrl?.length || 0), 0);
      if (totalImageLength - currentImageLength + imageDataUrl.length > 400000)
        throw new Error("Board artwork is limited to 400 KB total. Remove an image or use smaller pictures.");
      boardTiles[tileIndex].imageDataUrl = imageDataUrl;
      renderModeSettings();
      notify("Tile artwork compressed and added.");
    } catch (error) {
      notify(error.message);
      event.target.value = "";
    }
  }
});

async function hostQuiz(quiz) {
  const created = await post("/api/games", {
    quizName: quiz.quizName,
    description: quiz.description,
    theme: quiz.theme,
    mode: quiz.mode || "Quiz",
    boardEnabled: Boolean(quiz.boardEnabled || quiz.mode === "Board"),
    subjectWheelEnabled: Boolean(quiz.subjectWheelEnabled),
    boardTiles: quiz.boardTiles || [],
    boardTitle: quiz.boardTitle || "The Showdown",
    boardLayout: quiz.boardLayout || "Track",
    boardStyle: quiz.boardStyle || "Aurora",
    questions: quiz.questions
  });
  sessionStorage.removeItem("paradox-player");
  const room = { pin: created.pin, hostKey: created.hostKey };
  sessionStorage.setItem("paradox-host", JSON.stringify(room));
  setPath(`/host/${room.pin}`);
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
      setPath(`/play/${room.pin}`);
      startPolling("player", room);
    } catch (error) {
      button.disabled = false;
      notify(error.message);
    }
    return;
  }

  if (form.dataset.form === "answer-text") {
    const room = JSON.parse(sessionStorage.getItem("paradox-player") || "null");
    const answerText = new FormData(form).get("answerText")?.toString().trim();
    if (!room || !answerText) return notify("Type an answer before submitting.");
    const button = form.querySelector('button[type="submit"]');
    button.disabled = true;
    try {
      await post(`/api/games/${room.pin}/answers`, { answerText }, { "X-Player-Id": room.playerId });
      await refreshRoom();
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
