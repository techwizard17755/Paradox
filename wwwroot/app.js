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
  return {
    prompt: "",
    type: "MultipleChoice",
    options: ["", ""],
    correctOptionIndex: 0,
    acceptedAnswers: [""],
    timeLimitSeconds: 20
  };
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
    const existing = questions[index] || newQuestion();
    const type = card.querySelector(".question-type-select").value;
    const choiceFields = [...card.querySelectorAll(".answer-choice-field")];
    const acceptedFields = [...card.querySelectorAll(".accepted-answer-field")];
    const selected = card.querySelector('input[type="radio"]:checked');
    return {
      prompt: card.querySelector(".question-prompt").value,
      type,
      options: choiceFields.length ? choiceFields.map(input => input.value) : existing.options || [],
      correctOptionIndex: selected ? Number(selected.value) : existing.correctOptionIndex || 0,
      acceptedAnswers: acceptedFields.length ? acceptedFields.map(input => input.value) : existing.acceptedAnswers || [""],
      timeLimitSeconds: Number(card.querySelector(".question-footer .timer-select").value)
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

function renderWinnerAnnouncement(players, username = null, animate = false) {
  const winners = gameWinners(players);
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

function renderHost(snapshot, transition) {
  const host = JSON.parse(sessionStorage.getItem("paradox-host") || "null");
  if (!host) return renderJoin();
  setTheme(snapshot.theme);
  setNav("");
  const lobby = snapshot.status === "Lobby";
  const finished = snapshot.status === "Finished";
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
          ${renderWinnerAnnouncement(players, null, transition?.type === "finish")}
          <h2 class="leaderboard-heading">Final leaderboard</h2>
          ${scoreList}
          <div style="display:flex;justify-content:center;margin-top:22px"><button class="primary-btn" data-action="new-quiz">Create another quiz</button></div>
        </div>
      ` : `
        <div class="game-panel">
          <div class="game-meta"><span>${snapshot.status === "Reveal" ? "ANSWER REVEAL" : "QUESTION " + snapshot.questionNumber + " OF " + snapshot.questionCount}</span><strong class="question-clock">${snapshot.status === "Question" ? secondsLeft(snapshot.endsAtUtc) + " sec" : snapshot.playerCount + " playing"}</strong></div>
          <div class="timer-track"><div class="timer-fill" style="width:${timerPercent(snapshot)}%"></div></div>
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
          ${snapshot.status === "Reveal" ? `<div style="display:flex;justify-content:center;margin:20px 0 27px"><button class="primary-btn" data-action="next">${snapshot.questionNumber >= snapshot.questionCount ? "See final scores" : "Next question"} <span aria-hidden="true">→</span></button></div>` : ""}
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
  const players = snapshot.players || [];
  const playerScore = players.find(entry => entry.username.toLocaleLowerCase() === player.username.toLocaleLowerCase());
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
          ${renderWinnerAnnouncement(players, player.username, transition?.type === "finish")}
          <h2 class="leaderboard-heading">Final leaderboard</h2>${scoreList}
          <div style="display:flex;justify-content:center;margin-top:24px"><button class="secondary-btn" data-action="leave">Back to Paradox</button></div>
        </div>
      ` : `
        <div class="game-panel">
          <div class="game-meta"><span>${snapshot.status === "Reveal" ? "ANSWER REVEAL" : "QUESTION " + snapshot.questionNumber + " OF " + snapshot.questionCount}</span><strong class="question-clock">${snapshot.status === "Question" ? secondsLeft(snapshot.endsAtUtc) + " sec" : "SCORES"}</strong></div>
          <div class="timer-track"><div class="timer-fill" style="width:${timerPercent(snapshot)}%"></div></div>
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
    snapshot.surveyResponses
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
  if (event.target.matches(".question-type-select")) {
    syncQuestionsFromForm();
    drawQuestionCards();
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
