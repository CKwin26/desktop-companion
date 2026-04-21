const STORAGE_KEY = "task-familiar-v1";
const MAX_LOG_ITEMS = 60;

const priorityLabelMap = {
  high: "高优先级",
  medium: "中优先级",
  low: "低优先级",
};

function createDefaultState() {
  return {
    petName: "团子",
    settings: {
      reminderMinutes: 45,
      reviewMinutes: 120,
      focusMinutes: 25,
    },
    tasks: [],
    activityLog: [],
    lastReminderAt: Date.now(),
    lastReviewAt: Date.now(),
    focusSession: null,
    lastReviewDigest: null,
  };
}

const refs = {};
let state = loadState();
let heartbeatHandle = null;

bootstrap();

function bootstrap() {
  collectRefs();
  bindEvents();
  renderAll();
  startHeartbeat();
}

function collectRefs() {
  refs.petAvatar = document.getElementById("petAvatar");
  refs.petSpeech = document.getElementById("petSpeech");
  refs.metricTotal = document.getElementById("metricTotal");
  refs.metricDoing = document.getElementById("metricDoing");
  refs.metricDone = document.getElementById("metricDone");
  refs.metricRisk = document.getElementById("metricRisk");
  refs.reviewCard = document.getElementById("reviewCard");
  refs.reviewTimestamp = document.getElementById("reviewTimestamp");
  refs.todoCount = document.getElementById("todoCount");
  refs.doingCount = document.getElementById("doingCount");
  refs.blockedCount = document.getElementById("blockedCount");
  refs.doneCount = document.getElementById("doneCount");
  refs.laneTodo = document.getElementById("laneTodo");
  refs.laneDoing = document.getElementById("laneDoing");
  refs.laneBlocked = document.getElementById("laneBlocked");
  refs.laneDone = document.getElementById("laneDone");
  refs.activityLog = document.getElementById("activityLog");
  refs.completionBar = document.getElementById("completionBar");
  refs.completionText = document.getElementById("completionText");
  refs.focusTimerStatus = document.getElementById("focusTimerStatus");
  refs.grantNotificationsBtn = document.getElementById("grantNotificationsBtn");
  refs.forceReviewBtn = document.getElementById("forceReviewBtn");
  refs.clearLogBtn = document.getElementById("clearLogBtn");
  refs.focusModeBtn = document.getElementById("focusModeBtn");
  refs.taskForm = document.getElementById("taskForm");
  refs.settingsForm = document.getElementById("settingsForm");
  refs.petNameInput = document.getElementById("petNameInput");
  refs.reminderMinutesInput = document.getElementById("reminderMinutesInput");
  refs.reviewMinutesInput = document.getElementById("reviewMinutesInput");
  refs.focusMinutesInput = document.getElementById("focusMinutesInput");
}

function bindEvents() {
  refs.taskForm.addEventListener("submit", handleTaskCreate);
  refs.settingsForm.addEventListener("submit", handleSettingsSave);
  refs.forceReviewBtn.addEventListener("click", () => runReview("manual"));
  refs.clearLogBtn.addEventListener("click", clearLog);
  refs.focusModeBtn.addEventListener("click", toggleFocusMode);
  refs.grantNotificationsBtn.addEventListener("click", requestNotifications);

  [refs.laneTodo, refs.laneDoing, refs.laneBlocked, refs.laneDone].forEach((lane) => {
    lane.addEventListener("click", handleLaneClick);
    lane.addEventListener("submit", handleLaneSubmit);
  });
}

function loadState() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return createDefaultState();
    }

    const parsed = JSON.parse(raw);
    return {
      ...createDefaultState(),
      ...parsed,
      settings: {
        ...createDefaultState().settings,
        ...(parsed.settings || {}),
      },
      tasks: Array.isArray(parsed.tasks) ? parsed.tasks : [],
      activityLog: Array.isArray(parsed.activityLog) ? parsed.activityLog : [],
    };
  } catch (error) {
    console.warn("Failed to load state, using defaults.", error);
    return createDefaultState();
  }
}

function saveState() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

function handleTaskCreate(event) {
  event.preventDefault();
  const formData = new FormData(event.currentTarget);
  const title = String(formData.get("title") || "").trim();

  if (!title) {
    return;
  }

  const task = {
    id: createId(),
    title,
    category: String(formData.get("category") || "").trim(),
    priority: String(formData.get("priority") || "medium"),
    minutes: clampNumber(formData.get("minutes"), 30, 5, 480),
    dueAt: String(formData.get("dueAt") || "").trim(),
    status: "todo",
    createdAt: Date.now(),
    updatedAt: Date.now(),
    lastCheckInAt: null,
    latestNote: "",
  };

  state.tasks.unshift(task);
  logActivity(`接收新任务：${task.title}`);
  refs.taskForm.reset();
  document.getElementById("taskMinutes").value = "30";
  document.getElementById("taskPriority").value = "medium";
  state.lastReviewAt = Date.now();
  saveState();
  renderAll();
}

function handleSettingsSave(event) {
  event.preventDefault();

  state.petName = refs.petNameInput.value.trim() || createDefaultState().petName;
  state.settings.reminderMinutes = clampNumber(refs.reminderMinutesInput.value, 45, 5, 240);
  state.settings.reviewMinutes = clampNumber(refs.reviewMinutesInput.value, 120, 15, 480);
  state.settings.focusMinutes = clampNumber(refs.focusMinutesInput.value, 25, 10, 90);

  logActivity(
    `${state.petName} 的监督节奏已更新：${state.settings.reminderMinutes} 分钟催办一次，${state.settings.reviewMinutes} 分钟梳理一次。`
  );
  saveState();
  renderAll();
}

function handleLaneClick(event) {
  const actionButton = event.target.closest("[data-action]");
  if (!actionButton) {
    return;
  }

  const { action, taskId } = actionButton.dataset;
  const task = state.tasks.find((item) => item.id === taskId);
  if (!task) {
    return;
  }

  if (action === "delete") {
    state.tasks = state.tasks.filter((item) => item.id !== taskId);
    logActivity(`移除了任务：${task.title}`);
  }

  if (action === "to-doing") {
    updateTaskStatus(task, "doing", `开始推进：${task.title}`);
  }

  if (action === "to-blocked") {
    updateTaskStatus(task, "blocked", `任务卡住了：${task.title}`);
  }

  if (action === "to-done") {
    updateTaskStatus(task, "done", `完成任务：${task.title}`);
  }

  if (action === "to-todo") {
    updateTaskStatus(task, "todo", `重新放回待启动：${task.title}`);
  }

  saveState();
  renderAll();
}

function handleLaneSubmit(event) {
  const noteForm = event.target.closest(".note-form");
  if (!noteForm) {
    return;
  }

  event.preventDefault();
  const input = noteForm.querySelector("input");
  const note = input.value.trim();
  const taskId = noteForm.dataset.taskId;
  const task = state.tasks.find((item) => item.id === taskId);

  if (!task || !note) {
    return;
  }

  task.latestNote = note;
  task.lastCheckInAt = Date.now();
  task.updatedAt = Date.now();
  logActivity(`任务跟进：${task.title} / ${note}`);
  input.value = "";
  saveState();
  renderAll();
}

function updateTaskStatus(task, nextStatus, logMessage) {
  task.status = nextStatus;
  task.updatedAt = Date.now();
  if (nextStatus === "doing" && !task.lastCheckInAt) {
    task.lastCheckInAt = Date.now();
  }
  logActivity(logMessage);
}

function clearLog() {
  state.activityLog = [];
  saveState();
  renderAll();
}

function toggleFocusMode() {
  if (state.focusSession && state.focusSession.endsAt > Date.now()) {
    state.focusSession = null;
    logActivity("专注冲刺已手动结束。");
    saveState();
    renderAll();
    return;
  }

  const durationMinutes = state.settings.focusMinutes;
  state.focusSession = {
    startedAt: Date.now(),
    endsAt: Date.now() + durationMinutes * 60 * 1000,
  };
  logActivity(`开启 ${durationMinutes} 分钟专注冲刺。`);
  saveState();
  renderAll();
}

function requestNotifications() {
  if (!("Notification" in window)) {
    logActivity("当前环境不支持系统通知，宠物会继续在页面内提醒。");
    renderAll();
    return;
  }

  Notification.requestPermission().then((permission) => {
    if (permission === "granted") {
      logActivity("桌面提醒已开启，宠物会在关键节点冒出来。");
    } else {
      logActivity("未授予桌面提醒权限，仍会保留页面内监督。");
    }
    renderAll();
  });
}

function startHeartbeat() {
  if (heartbeatHandle) {
    window.clearInterval(heartbeatHandle);
  }

  heartbeatHandle = window.setInterval(() => {
    const now = Date.now();
    let changed = false;

    if (state.focusSession && state.focusSession.endsAt <= now) {
      state.focusSession = null;
      logActivity("本轮专注冲刺结束，准备做一次状态回报。");
      maybeNotify(`${state.petName} 提醒`, "专注冲刺结束了，回来更新一下任务状态吧。");
      changed = true;
    }

    const reminderInterval = state.settings.reminderMinutes * 60 * 1000;
    const reviewInterval = state.settings.reviewMinutes * 60 * 1000;

    if (state.tasks.length > 0 && now - state.lastReminderAt >= reminderInterval) {
      const reminderMessage = createReminderMessage();
      logActivity(reminderMessage);
      refs.petSpeech.textContent = reminderMessage;
      state.lastReminderAt = now;
      maybeNotify(`${state.petName} 的催办`, reminderMessage);
      changed = true;
    }

    if (state.tasks.length > 0 && now - state.lastReviewAt >= reviewInterval) {
      runReview("auto");
      changed = true;
    }

    if (changed) {
      saveState();
      renderAll();
      return;
    }

    renderFocusStatus();
  }, 1000);
}

function runReview(source) {
  state.lastReviewDigest = buildReviewDigest();
  state.lastReviewAt = Date.now();

  const prefix = source === "auto" ? "自动梳理已更新。" : "手动梳理已完成。";
  logActivity(`${prefix} ${state.lastReviewDigest.headline}`);
  maybeNotify(`${state.petName} 的梳理结果`, state.lastReviewDigest.headline);
  saveState();
  renderAll();
}

function buildReviewDigest() {
  const metrics = getMetrics();
  const tasksByStatus = groupTasksByStatus();
  const topTask = [...state.tasks]
    .filter((task) => task.status !== "done")
    .sort(compareTaskUrgency)[0];

  let headline = "节奏稳住了，继续往前推。";
  let summary = "任务分布比较均衡，可以保持当前推进方式。";

  if (metrics.overdueCount > 0) {
    headline = `先救火：有 ${metrics.overdueCount} 个任务已经过线。`;
    summary = "先处理最早截止且高优先级的任务，其他任务暂时不要再扩散。";
  } else if (metrics.blockedCount > 0) {
    headline = `先解阻：有 ${metrics.blockedCount} 个任务卡住了。`;
    summary = "优先把阻塞点写清楚，决定是求助、拆分还是延期，不要让它们闷着不动。";
  } else if (metrics.doingCount === 0 && metrics.todoCount > 0) {
    headline = "下一步很明确：从待启动里只挑 1 件开始。";
    summary = "别同时点燃太多任务，先给宠物一个正在推进的目标。";
  } else if (metrics.doneCount === metrics.total && metrics.total > 0) {
    headline = "清单清空了，今天收尾很漂亮。";
    summary = "可以补一条复盘记录，或者提前布置明天的第一件事。";
  }

  const bullets = [];

  if (topTask) {
    bullets.push(`现在最该盯住的是「${topTask.title}」。`);
  }

  if (tasksByStatus.blocked.length > 0) {
    bullets.push(`阻塞最多的区域：${summarizeCategories(tasksByStatus.blocked)}。`);
  }

  if (metrics.doingCount > 0) {
    bullets.push(`推进中 ${metrics.doingCount} 件，记得只保留少量主线任务。`);
  }

  if (metrics.doneCount > 0) {
    bullets.push(`已完成 ${metrics.doneCount} 件，推进势能正在累积。`);
  }

  if (bullets.length === 0) {
    bullets.push("把第一件任务加进来后，宠物就能开始形成监督节奏。");
  }

  return {
    headline,
    summary,
    bullets,
    createdAt: Date.now(),
  };
}

function createReminderMessage() {
  const metrics = getMetrics();
  const doingTask = state.tasks.find((task) => task.status === "doing");
  const overdueTask = [...state.tasks].filter(isTaskOverdue).sort(compareTaskUrgency)[0];
  const blockedTask = state.tasks.find((task) => task.status === "blocked");

  if (overdueTask) {
    return `${state.petName} 盯到「${overdueTask.title}」已经逾期，先把它处理或重新定档。`;
  }

  if (blockedTask) {
    return `${state.petName} 想知道「${blockedTask.title}」卡在哪一步，写一条跟进备注吧。`;
  }

  if (doingTask) {
    return `${state.petName} 在盯「${doingTask.title}」，记得更新一下进展，别让推进中变成摆设。`;
  }

  if (metrics.todoCount > 0) {
    return `${state.petName} 在等你点亮第一件任务，先从最重要的那一件开始。`;
  }

  return `${state.petName} 很满意，当前清单已经处理干净。`;
}

function logActivity(message) {
  state.activityLog.unshift({
    id: createId(),
    message,
    createdAt: Date.now(),
  });
  state.activityLog = state.activityLog.slice(0, MAX_LOG_ITEMS);
}

function renderAll() {
  hydrateSettingsForm();
  renderMetrics();
  renderBoard();
  renderReview();
  renderSpeech();
  renderActivityLog();
  renderFocusStatus();
  syncNotificationButton();
  saveState();
}

function hydrateSettingsForm() {
  refs.petNameInput.value = state.petName;
  refs.reminderMinutesInput.value = String(state.settings.reminderMinutes);
  refs.reviewMinutesInput.value = String(state.settings.reviewMinutes);
  refs.focusMinutesInput.value = String(state.settings.focusMinutes);
}

function renderMetrics() {
  const metrics = getMetrics();
  refs.metricTotal.textContent = String(metrics.total);
  refs.metricDoing.textContent = String(metrics.doingCount);
  refs.metricDone.textContent = String(metrics.doneCount);
  refs.metricRisk.textContent = String(metrics.overdueCount + metrics.blockedCount);

  const percent = metrics.total > 0 ? Math.round((metrics.doneCount / metrics.total) * 100) : 0;
  refs.completionBar.style.width = `${percent}%`;
  refs.completionText.textContent = `${percent}%`;

  refs.petAvatar.className = `pet-avatar ${resolveMoodClass(metrics)}`;
}

function renderBoard() {
  const grouped = groupTasksByStatus();

  refs.todoCount.textContent = String(grouped.todo.length);
  refs.doingCount.textContent = String(grouped.doing.length);
  refs.blockedCount.textContent = String(grouped.blocked.length);
  refs.doneCount.textContent = String(grouped.done.length);

  renderLane(refs.laneTodo, grouped.todo, "还没开始的任务会待在这里。");
  renderLane(refs.laneDoing, grouped.doing, "开始一件任务后，宠物会重点盯这里。");
  renderLane(refs.laneBlocked, grouped.blocked, "卡住的任务先丢到这里，方便集中处理。");
  renderLane(refs.laneDone, grouped.done, "完成后的任务会在这里堆出成就感。");
}

function renderLane(container, tasks, emptyText) {
  if (tasks.length === 0) {
    container.innerHTML = `<p class="empty-lane">${emptyText}</p>`;
    return;
  }

  container.innerHTML = tasks.sort(compareTaskUrgency).map(renderTaskCard).join("");
}

function renderTaskCard(task) {
  const overdue = isTaskOverdue(task);
  const stale = isTaskStale(task);
  const classes = [
    "task-card",
    `priority-${task.priority}`,
    overdue ? "overdue" : "",
    stale ? "stale" : "",
  ]
    .filter(Boolean)
    .join(" ");

  const metaParts = [];
  if (task.category) {
    metaParts.push(task.category);
  }
  metaParts.push(`预计 ${task.minutes} 分钟`);
  if (task.dueAt) {
    metaParts.push(`截止 ${formatDateTime(task.dueAt)}`);
  }

  return `
    <article class="${classes}">
      <div class="task-top">
        <div>
          <span class="priority-badge">${priorityLabelMap[task.priority] || priorityLabelMap.medium}</span>
          <h4>${escapeHtml(task.title)}</h4>
        </div>
        <button class="task-delete" data-action="delete" data-task-id="${task.id}" type="button" aria-label="删除任务">删除</button>
      </div>
      <p class="task-meta">${escapeHtml(metaParts.join(" · "))}</p>
      <p class="task-timestamp">最近变更：${formatRelative(task.updatedAt)}</p>
      <p class="task-note-preview">${task.latestNote ? `最新记录：${escapeHtml(task.latestNote)}` : "还没有进度备注。"}</p>
      <form class="note-form" data-task-id="${task.id}">
        <input type="text" maxlength="80" placeholder="补一条进度：我刚做到了哪？">
        <button type="submit">记录</button>
      </form>
      <div class="task-actions">
        ${renderTaskActions(task)}
      </div>
    </article>
  `;
}

function renderTaskActions(task) {
  if (task.status === "todo") {
    return `
      <button class="action-primary" data-action="to-doing" data-task-id="${task.id}" type="button">开始</button>
      <button data-action="to-blocked" data-task-id="${task.id}" type="button">标记阻塞</button>
      <button class="action-done" data-action="to-done" data-task-id="${task.id}" type="button">直接完成</button>
    `;
  }

  if (task.status === "doing") {
    return `
      <button data-action="to-todo" data-task-id="${task.id}" type="button">放回待启动</button>
      <button class="action-alert" data-action="to-blocked" data-task-id="${task.id}" type="button">遇到阻塞</button>
      <button class="action-done" data-action="to-done" data-task-id="${task.id}" type="button">完成</button>
    `;
  }

  if (task.status === "blocked") {
    return `
      <button class="action-primary" data-action="to-doing" data-task-id="${task.id}" type="button">恢复推进</button>
      <button data-action="to-todo" data-task-id="${task.id}" type="button">暂缓</button>
      <button class="action-done" data-action="to-done" data-task-id="${task.id}" type="button">完成</button>
    `;
  }

  return `
    <button data-action="to-todo" data-task-id="${task.id}" type="button">重新打开</button>
  `;
}

function renderReview() {
  const digest = state.lastReviewDigest || buildReviewDigest();
  refs.reviewTimestamp.textContent = `最近更新 ${formatRelative(digest.createdAt)}`;

  refs.reviewCard.innerHTML = `
    <h3>${escapeHtml(digest.headline)}</h3>
    <p>${escapeHtml(digest.summary)}</p>
    <ul class="review-list">
      ${digest.bullets.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}
    </ul>
  `;
}

function renderSpeech() {
  refs.petSpeech.textContent = createReminderMessage();
}

function renderActivityLog() {
  if (state.activityLog.length === 0) {
    refs.activityLog.innerHTML = `
      <li class="activity-item">
        <span class="activity-time">刚刚</span>
        <strong>宠物监督记录会出现在这里。</strong>
      </li>
    `;
    return;
  }

  refs.activityLog.innerHTML = state.activityLog
    .map(
      (entry) => `
        <li class="activity-item">
          <span class="activity-time">${formatDateTime(entry.createdAt)}</span>
          <strong>${escapeHtml(entry.message)}</strong>
        </li>
      `
    )
    .join("");
}

function renderFocusStatus() {
  const session = state.focusSession;
  if (!session || session.endsAt <= Date.now()) {
    refs.focusTimerStatus.textContent = "未开始";
    refs.focusModeBtn.textContent = "开始专注冲刺";
    return;
  }

  const remainingSeconds = Math.max(0, Math.floor((session.endsAt - Date.now()) / 1000));
  refs.focusTimerStatus.textContent = `${formatDuration(remainingSeconds)} 剩余`;
  refs.focusModeBtn.textContent = "结束专注冲刺";
}

function syncNotificationButton() {
  if (!("Notification" in window)) {
    refs.grantNotificationsBtn.textContent = "环境不支持提醒";
    refs.grantNotificationsBtn.disabled = true;
    return;
  }

  const permission = Notification.permission;
  refs.grantNotificationsBtn.disabled = permission === "granted";
  refs.grantNotificationsBtn.textContent = permission === "granted" ? "提醒已开启" : "开启提醒";
}

function getMetrics() {
  const grouped = groupTasksByStatus();
  const overdueCount = state.tasks.filter(isTaskOverdue).length;

  return {
    total: state.tasks.length,
    todoCount: grouped.todo.length,
    doingCount: grouped.doing.length,
    blockedCount: grouped.blocked.length,
    doneCount: grouped.done.length,
    overdueCount,
  };
}

function groupTasksByStatus() {
  return state.tasks.reduce(
    (groups, task) => {
      if (!groups[task.status]) {
        groups.todo.push(task);
        return groups;
      }

      groups[task.status].push(task);
      return groups;
    },
    {
      todo: [],
      doing: [],
      blocked: [],
      done: [],
    }
  );
}

function compareTaskUrgency(a, b) {
  const priorityScore = {
    high: 0,
    medium: 1,
    low: 2,
  };
  const overdueDelta = Number(isTaskOverdue(a)) - Number(isTaskOverdue(b));
  if (overdueDelta !== 0) {
    return overdueDelta < 0 ? 1 : -1;
  }

  const dueA = a.dueAt ? new Date(a.dueAt).getTime() : Number.MAX_SAFE_INTEGER;
  const dueB = b.dueAt ? new Date(b.dueAt).getTime() : Number.MAX_SAFE_INTEGER;
  if (dueA !== dueB) {
    return dueA - dueB;
  }

  return priorityScore[a.priority] - priorityScore[b.priority] || a.createdAt - b.createdAt;
}

function resolveMoodClass(metrics) {
  if (metrics.overdueCount > 0) {
    return "pet-mood-worried";
  }
  if (metrics.blockedCount > 0) {
    return "pet-mood-alert";
  }
  if (metrics.total > 0 && metrics.doneCount >= Math.ceil(metrics.total / 2)) {
    return "pet-mood-spark";
  }
  return "pet-mood-rest";
}

function isTaskOverdue(task) {
  if (!task.dueAt || task.status === "done") {
    return false;
  }

  const dueTime = new Date(task.dueAt).getTime();
  return Number.isFinite(dueTime) && dueTime < Date.now();
}

function isTaskStale(task) {
  if (task.status === "done") {
    return false;
  }
  return Date.now() - task.updatedAt > state.settings.reminderMinutes * 60 * 1000;
}

function summarizeCategories(tasks) {
  const counts = tasks.reduce((map, task) => {
    const key = task.category || "未分类";
    map.set(key, (map.get(key) || 0) + 1);
    return map;
  }, new Map());

  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 2)
    .map(([name, count]) => `${name} (${count})`)
    .join("、");
}

function maybeNotify(title, body) {
  if (!("Notification" in window) || Notification.permission !== "granted") {
    return;
  }

  new Notification(title, { body });
}

function formatDateTime(value) {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) {
    return "未设置";
  }
  return new Intl.DateTimeFormat("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatRelative(value) {
  const deltaMs = Date.now() - new Date(value).getTime();
  const minutes = Math.floor(deltaMs / (60 * 1000));
  if (minutes <= 0) {
    return "刚刚";
  }
  if (minutes < 60) {
    return `${minutes} 分钟前`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} 小时前`;
  }

  const days = Math.floor(hours / 24);
  return `${days} 天前`;
}

function formatDuration(totalSeconds) {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

function clampNumber(value, fallback, min, max) {
  const nextValue = Number(value);
  if (!Number.isFinite(nextValue)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, Math.round(nextValue)));
}

function createId() {
  if (window.crypto && typeof window.crypto.randomUUID === "function") {
    return window.crypto.randomUUID();
  }
  return `task-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
