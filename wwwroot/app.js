'use strict';

// ================= Configuración =================
const MATCH_ROUNDS = 10;
const ROUND_TIME_MS = 10000;      // tiempo por ronda en "Partida de 10"
const REQUIRED_HOLD_MS = 380;     // tiempo que hay que mantener la cabeza para responder
const DET_INTERVAL_MS = 33;       // ~30 FPS de detección: más no aporta (la webcam va a 30) y gasta CPU
const AUTO_NEXT_MS = 2000;        // avance automático con control facial

const $ = id => document.getElementById(id);
const el = {
  points: $('points'), score: $('score'), accuracy: $('accuracy'), streak: $('streak'), best: $('best'),
  totalProfiles: $('totalProfiles'), roundLabel: $('roundLabel'),
  timer: $('timer'), timerFill: $('timerFill'), timerText: $('timerText'),
  portrait: $('portrait'), wrap: $('portraitWrap'), reveal: $('reveal'), pop: $('pop'), feedback: $('feedback'),
  btnLi: $('btnLi'), btnIn: $('btnIn'), next: $('nextBtn'), history: $('history'),
  gray: $('chkGray'), blur: $('chkBlur'), sound: $('btnSound'),
  video: $('webcam'), canvas: $('camPreview'), camOff: $('camOff'), hold: $('holdBar'),
  hudCamFps: $('hudCamFps'), hudDetFps: $('hudDetFps'), hudLatency: $('hudLatency'), hudAngle: $('hudAngle'),
  face: $('btnFaceControl'), scan: $('btnScan'), scanResult: $('scanResult'), toast: $('toast'),
  summary: $('summary'), sumRank: $('summaryRank'), sumPoints: $('sumPoints'), sumAccuracy: $('sumAccuracy'),
  sumStreak: $('sumStreak'), sumRecord: $('sumRecord')
};
const modeButtons = document.querySelectorAll('.segmented [data-mode]');
const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

const store = {
  get(k, d) { try { const v = localStorage.getItem(k); return v === null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* modo privado */ } }
};

// ================= Estado =================
const state = {
  mode: store.get('lioi.mode', 'free'),
  sound: store.get('lioi.sound', true),
  bestPoints: store.get('lioi.bestPoints', 0),
  id: null, canGuess: false, answered: false,
  points: 0, score: 0, total: 0, streak: 0, maxStreak: 0, history: [],
  roundStart: 0, timerRaf: 0, lastTimerSec: -1,
  nextTimer: 0, prefetch: null
};

// ================= Utilidades =================
function toast(msg) {
  el.toast.textContent = msg;
  el.toast.classList.add('show');
  clearTimeout(toast.t);
  toast.t = setTimeout(() => el.toast.classList.remove('show'), 2600);
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function restartAnimation(node, cls) {
  node.classList.remove(cls);
  void node.offsetWidth; // fuerza reflow para reiniciar la animación CSS
  node.classList.add(cls);
}

let audioCtx = null;
function beep(freqs, dur = 0.09) {
  if (!state.sound) return;
  try {
    audioCtx ??= new AudioContext();
    const t0 = audioCtx.currentTime;
    freqs.forEach((f, i) => {
      const o = audioCtx.createOscillator(), g = audioCtx.createGain();
      const start = t0 + i * dur;
      o.frequency.value = f;
      g.gain.setValueAtTime(0.0001, start);
      g.gain.exponentialRampToValueAtTime(0.12, start + 0.01);
      g.gain.exponentialRampToValueAtTime(0.0001, start + dur);
      o.connect(g).connect(audioCtx.destination);
      o.start(start);
      o.stop(start + dur + 0.02);
    });
  } catch { /* sin audio */ }
}
const sfx = { ok: () => beep([660, 880]), bad: () => beep([240, 160], 0.12), end: () => beep([523, 659, 784, 1046], 0.1) };

// ================= Render de estadísticas =================
const lastShown = {};
function setStat(node, key, value) {
  if (lastShown[key] === value) return; // evita escrituras DOM innecesarias
  const first = !(key in lastShown);
  lastShown[key] = value;
  node.textContent = value;
  if (!first && !reduceMotion && key !== 'accuracy') restartAnimation(node, 'bump');
}

function renderStats() {
  setStat(el.points, 'points', state.points);
  setStat(el.score, 'score', state.score);
  setStat(el.accuracy, 'accuracy', state.total ? Math.round(state.score / state.total * 100) + '%' : '–');
  setStat(el.streak, 'streak', state.streak);
  setStat(el.best, 'best', state.bestPoints);
  el.history.innerHTML = state.history.slice(-10).map(ok =>
    `<li class="${ok ? 'ok' : 'bad'}" title="${ok ? 'Acierto' : 'Fallo'}">${ok ? '✓' : '✗'}</li>`).join('');
}

function renderRoundLabel() {
  const n = Math.min(state.total + 1, MATCH_ROUNDS);
  el.roundLabel.textContent = state.mode === 'match' ? `Ronda ${n}/${MATCH_ROUNDS}` : `Ronda ${state.total + 1}`;
}

function setChoicesEnabled(on) { el.btnLi.disabled = el.btnIn.disabled = !on; }
const isMatchOver = () => state.mode === 'match' && state.total >= MATCH_ROUNDS;

// ================= Temporizador (solo partida) =================
function startTimer() {
  if (state.mode !== 'match') return;
  cancelAnimationFrame(state.timerRaf);
  state.lastTimerSec = -1;
  const tick = () => {
    const left = ROUND_TIME_MS - (performance.now() - state.roundStart);
    el.timerFill.style.transform = `scaleX(${Math.max(0, left / ROUND_TIME_MS)})`;
    const sec = Math.max(0, Math.ceil(left / 1000));
    if (sec !== state.lastTimerSec) {
      state.lastTimerSec = sec;
      el.timerText.textContent = sec;
      el.timer.classList.toggle('urgent', sec <= 3);
    }
    if (left <= 0) { submitGuess('Timeout'); return; }
    state.timerRaf = requestAnimationFrame(tick);
  };
  state.timerRaf = requestAnimationFrame(tick);
}
function stopTimer() { cancelAnimationFrame(state.timerRaf); el.timer.classList.remove('urgent'); }

// ================= Rondas =================
const imageUrl = id => `/api/image/${id}?grayscale=${el.gray.checked}&blur=${el.blur.checked}`;

function fetchRound(exclude) {
  return fetch(exclude ? `/api/round?exclude=${exclude}` : '/api/round').then(r => {
    if (!r.ok) throw new Error(r.status);
    return r.json();
  });
}

// Pide la siguiente ronda y precarga su imagen mientras el jugador lee el resultado
function prefetchNext() {
  const p = fetchRound(state.id);
  state.prefetch = p;
  p.then(d => { const img = new Image(); img.decoding = 'async'; img.src = imageUrl(d.id); })
   .catch(() => { if (state.prefetch === p) state.prefetch = null; });
}

async function nextRound() {
  clearTimeout(state.nextTimer);
  if (isMatchOver()) { showSummary(); return; }

  state.canGuess = false;
  state.answered = false;
  stopTimer();
  setChoicesEnabled(false);
  el.next.hidden = true;
  el.feedback.textContent = '';
  el.feedback.className = '';
  el.wrap.classList.remove('correct', 'wrong');
  el.reveal.classList.remove('show');
  el.timer.hidden = state.mode !== 'match';
  el.timerFill.style.transform = 'scaleX(1)';
  renderRoundLabel();

  try {
    const pending = state.prefetch;
    state.prefetch = null;
    const d = await (pending ?? fetchRound(state.id));
    state.id = d.id;
    if (d.totalProfiles) el.totalProfiles.textContent = d.totalProfiles;
    loadImage();
  } catch {
    toast('No se pudo cargar la ronda. Reintentando…');
    state.nextTimer = setTimeout(nextRound, 1500);
  }
}

function loadImage() {
  if (!state.id) return;
  el.wrap.classList.add('is-loading');
  el.portrait.classList.add('loading');
  el.portrait.src = imageUrl(state.id);
}

el.portrait.addEventListener('load', () => {
  el.wrap.classList.remove('is-loading');
  el.portrait.classList.remove('loading');
  if (!state.answered && !state.canGuess) {
    state.canGuess = true;
    setChoicesEnabled(true);
    state.roundStart = performance.now();
    startTimer();
  }
});
el.portrait.addEventListener('error', () => {
  el.wrap.classList.remove('is-loading');
  if (state.id) toast('No se pudo cargar el retrato.');
});

async function submitGuess(guess) {
  if (!state.canGuess || !state.id) return;
  state.canGuess = false;
  state.answered = true;
  stopTimer();
  setChoicesEnabled(false);
  const elapsed = performance.now() - state.roundStart;

  try {
    const r = await fetch('/api/guess', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ id: state.id, guess })
    });
    if (!r.ok) throw new Error(r.status);
    const d = await r.json();

    state.total++;
    let gained = 0;
    if (d.correct) {
      state.score++;
      state.streak++;
      state.maxStreak = Math.max(state.maxStreak, state.streak);
      const multiplier = 1 + Math.min(state.streak - 1, 4) * 0.25;           // hasta x2 con racha de 5
      const speedBonus = state.mode === 'match' ? Math.round(Math.max(0, ROUND_TIME_MS - elapsed) / 100) : 0;
      gained = Math.round((100 + speedBonus) * multiplier);
      state.points += gained;
      if (state.points > state.bestPoints) { state.bestPoints = state.points; store.set('lioi.bestPoints', state.bestPoints); }
      sfx.ok();
    } else {
      state.streak = 0;
      sfx.bad();
    }
    state.history.push(d.correct);
    renderStats();

    el.wrap.classList.add(d.correct ? 'correct' : 'wrong');
    const catColor = d.actualCategory === 'LinkedIn' ? '#7bb8ff' : '#ff8a8a';
    el.reveal.innerHTML = `<strong style="color:${catColor}">${escapeHtml(d.actualCategory)}</strong>${escapeHtml(d.description)}`;
    el.reveal.classList.add('show');
    if (gained) { el.pop.textContent = `+${gained}`; restartAnimation(el.pop, 'show'); }

    el.feedback.className = d.correct ? 'ok' : 'bad';
    const lead = guess === 'Timeout' ? '¡Se acabó el tiempo!' : d.correct ? '¡Acertaste!' : 'Fallaste.';
    el.feedback.innerHTML = lead + (d.correct && state.streak > 1 ? `<span class="streak">Racha ×${state.streak}</span>` : '');

    const over = isMatchOver();
    el.next.textContent = over ? 'Ver resultados' : 'Siguiente ronda →';
    el.next.hidden = false;
    el.next.focus({ preventScroll: true });
    if (!over) prefetchNext();
    if (face.active || over) state.nextTimer = setTimeout(nextRound, over ? AUTO_NEXT_MS + 600 : AUTO_NEXT_MS);
  } catch {
    state.canGuess = true;
    state.answered = false;
    setChoicesEnabled(true);
    startTimer();
    toast('No se pudo enviar la respuesta. Inténtalo de nuevo.');
  }
}

// ================= Modos y resumen =================
function setMode(mode) {
  state.mode = mode;
  store.set('lioi.mode', mode);
  modeButtons.forEach(b => b.setAttribute('aria-pressed', b.dataset.mode === mode));
}

function startGame(mode) {
  clearTimeout(state.nextTimer);
  setMode(mode);
  Object.assign(state, { points: 0, score: 0, total: 0, streak: 0, maxStreak: 0, history: [] });
  renderStats();
  nextRound();
}

function rankFor(acc) {
  if (acc >= 90) return '🕵️ Agente especial de Interpol';
  if (acc >= 70) return '💼 Headhunter senior';
  if (acc >= 50) return '🎓 Becario con intuición';
  return '🚨 Te han hackeado el LinkedIn';
}

function showSummary() {
  stopTimer();
  const acc = state.total ? Math.round(state.score / state.total * 100) : 0;
  el.sumRank.textContent = rankFor(acc);
  el.sumPoints.textContent = state.points;
  el.sumAccuracy.textContent = acc + '%';
  el.sumStreak.textContent = state.maxStreak;
  el.sumRecord.textContent = state.points >= state.bestPoints && state.points > 0
    ? '¡Nuevo récord personal!' : `Récord: ${state.bestPoints} puntos`;
  sfx.end();
  if (!el.summary.open) el.summary.showModal();
}

modeButtons.forEach(b => b.addEventListener('click', () => {
  if (b.dataset.mode !== state.mode) startGame(b.dataset.mode);
}));
$('btnPlayAgain').addEventListener('click', () => { el.summary.close(); startGame('match'); });
$('btnFreeMode').addEventListener('click', () => { el.summary.close(); startGame('free'); });

el.sound.addEventListener('click', () => {
  state.sound = !state.sound;
  store.set('lioi.sound', state.sound);
  el.sound.setAttribute('aria-pressed', state.sound);
  el.sound.textContent = state.sound ? '🔊' : '🔇';
});

// ================= Entrada: botones, teclado y gestos =================
el.btnLi.addEventListener('click', () => submitGuess('LinkedIn'));
el.btnIn.addEventListener('click', () => submitGuess('Interpol'));
el.next.addEventListener('click', nextRound);
el.gray.addEventListener('change', loadImage);
el.blur.addEventListener('change', loadImage);

document.addEventListener('keydown', e => {
  if (el.summary.open || e.target.matches('input, textarea') || e.metaKey || e.ctrlKey || e.altKey) return;
  if (e.key === 'ArrowLeft') { e.preventDefault(); submitGuess('LinkedIn'); }
  else if (e.key === 'ArrowRight') { e.preventDefault(); submitGuess('Interpol'); }
  else if (e.key === 'Enter' && !el.next.hidden && document.activeElement !== el.next) nextRound();
});

// Deslizar la foto: izquierda = LinkedIn, derecha = Interpol (con arrastre visual)
let swipe = null;
el.wrap.addEventListener('pointerdown', e => {
  if (!state.canGuess) return;
  swipe = { x: e.clientX, id: e.pointerId };
  el.wrap.setPointerCapture(e.pointerId);
});
el.wrap.addEventListener('pointermove', e => {
  if (!swipe || e.pointerId !== swipe.id) return;
  const dx = e.clientX - swipe.x;
  el.portrait.style.transform = `translateX(${dx * 0.4}px) rotate(${dx * 0.03}deg)`;
});
const endSwipe = e => {
  if (!swipe || e.pointerId !== swipe.id) return;
  const dx = e.clientX - swipe.x;
  swipe = null;
  el.portrait.style.transform = '';
  if (e.type === 'pointerup' && Math.abs(dx) > 70) submitGuess(dx < 0 ? 'LinkedIn' : 'Interpol');
};
el.wrap.addEventListener('pointerup', endSwipe);
el.wrap.addEventListener('pointercancel', endSwipe);

// ================= Cámara y control facial =================
const ctx = el.canvas.getContext('2d', { alpha: false });
const face = { active: false, sending: false, target: null, render: null, action: 'Center', last: 'Center', holdStart: 0, profile: false };
let camReady = false, drawing = false, lastVideoTime = -1;
let frames = 0, lastFps = performance.now(), detCount = 0, lastDet = performance.now(), lastHoldP = -1;

// Lienzo pequeño para detección, con espejo fijado una sola vez
const small = Object.assign(document.createElement('canvas'), { width: 200, height: 150 });
const sctx = small.getContext('2d');
sctx.setTransform(-1, 0, 0, 1, small.width, 0);

// Capa estática de zonas y guías: se dibuja una vez y se compone con un solo drawImage
const overlay = document.createElement('canvas');
function buildOverlay(w, h) {
  overlay.width = w; overlay.height = h;
  const o = overlay.getContext('2d');
  const L = w * .38, R = w * .62;
  o.fillStyle = 'rgba(10,102,194,.16)'; o.fillRect(0, 0, L, h);
  o.fillStyle = 'rgba(200,30,30,.16)'; o.fillRect(R, 0, w - R, h);
  o.lineWidth = 2; o.setLineDash([8, 6]);
  o.strokeStyle = '#3b8eea'; o.beginPath(); o.moveTo(L, 0); o.lineTo(L, h); o.stroke();
  o.strokeStyle = '#f05252'; o.beginPath(); o.moveTo(R, 0); o.lineTo(R, h); o.stroke();
  o.font = '700 15px system-ui, sans-serif';
  o.fillStyle = '#7bb8ff'; o.fillText('← LINKEDIN', 14, h - 18);
  o.fillStyle = '#ff8a8a'; o.fillText('INTERPOL →', w - o.measureText('INTERPOL →').width - 14, h - 18);
}

async function initCamera() {
  try {
    const stream = await navigator.mediaDevices.getUserMedia({
      video: { width: { ideal: 640 }, height: { ideal: 480 }, frameRate: { ideal: 30, max: 60 } },
      audio: false
    });
    el.video.srcObject = stream;
    await el.video.play();
    el.canvas.width = el.video.videoWidth || 480;
    el.canvas.height = el.video.videoHeight || 360;
    buildOverlay(el.canvas.width, el.canvas.height);
    camReady = true;
    startDrawing();
  } catch {
    el.camOff.hidden = false;
    el.face.disabled = el.scan.disabled = true;
    ctx.fillStyle = '#0b1020';
    ctx.fillRect(0, 0, el.canvas.width, el.canvas.height);
  }
}

function startDrawing() {
  if (drawing || !camReady || document.hidden) return;
  drawing = true;
  requestAnimationFrame(drawFrame);
}

// Solo redibuja cuando la webcam entrega un fotograma nuevo (en pantallas de 120/144 Hz ahorra 75 % de trabajo)
function drawFrame(now) {
  if (document.hidden) { drawing = false; return; }
  requestAnimationFrame(drawFrame);

  if (face.active) updateHold();
  const vt = el.video.currentTime;
  if (vt === lastVideoTime) return;
  lastVideoTime = vt;

  frames++;
  if (now - lastFps >= 1000) {
    el.hudCamFps.textContent = Math.round(frames * 1000 / (now - lastFps));
    frames = 0; lastFps = now;
  }

  const w = el.canvas.width, h = el.canvas.height;
  ctx.setTransform(-1, 0, 0, 1, w, 0);
  ctx.drawImage(el.video, 0, 0, w, h);
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  if (face.active) { ctx.drawImage(overlay, 0, 0); drawFaceBox(w, h); }
}

function drawFaceBox(w, h) {
  const t = face.target;
  if (!t) { face.render = null; return; }
  const r = face.render || (face.render = { ...t });
  const k = reduceMotion ? 1 : .45;
  r.x += (t.x - r.x) * k; r.y += (t.y - r.y) * k; r.w += (t.w - r.w) * k; r.h += (t.h - r.h) * k;
  const bx = r.x * w, by = r.y * h, bw = r.w * w, bh = r.h * h;
  const color = face.action === 'LinkedIn' ? '#3b8eea' : face.action === 'Interpol' ? '#f05252' : '#34d399';
  ctx.lineWidth = 3; ctx.strokeStyle = color; ctx.strokeRect(bx, by, bw, bh);
  const label = (face.action === 'Center' ? 'CENTRO' : face.action.toUpperCase()) + (face.profile ? ' · PERFIL' : '');
  ctx.fillStyle = color; ctx.fillRect(bx, Math.max(0, by - 24), Math.max(110, bw), 22);
  ctx.fillStyle = '#fff'; ctx.font = '700 12px system-ui, sans-serif'; ctx.fillText(label, bx + 6, Math.max(15, by - 8));
}

function setHold(p, color) {
  const q = Math.round(p * 100) / 100;
  if (q === lastHoldP) return;
  lastHoldP = q;
  el.hold.style.transform = `scaleX(${q})`;
  if (color) el.hold.style.background = color;
}

function updateHold() {
  if (state.canGuess && (face.action === 'LinkedIn' || face.action === 'Interpol')) {
    const p = Math.min(1, (performance.now() - face.holdStart) / REQUIRED_HOLD_MS);
    setHold(p, face.action === 'LinkedIn' ? '#3b8eea' : '#f05252');
    if (p >= 1) {
      const choice = face.action;
      face.action = face.last = 'Center';
      setHold(0);
      submitGuess(choice);
    }
  } else setHold(0);
}

function setFaceControl(on) {
  face.active = on;
  el.face.setAttribute('aria-pressed', on);
  el.face.textContent = on ? 'Desactivar control facial' : 'Activar control facial';
  if (on) { detCount = 0; lastDet = performance.now(); loop(); }
  else { face.target = null; face.action = face.last = 'Center'; el.hudDetFps.textContent = 0; setHold(0); lastVideoTime = -1; }
}
el.face.addEventListener('click', () => setFaceControl(!face.active));

// Bucle de detección: un único envío en vuelo, JPEG en bruto (sin multipart) y ritmo limitado a ~30 FPS
function loop() {
  if (!face.active || document.hidden || !camReady || face.sending) return;
  face.sending = true;
  sctx.drawImage(el.video, 0, 0, small.width, small.height);
  const t0 = performance.now();
  small.toBlob(async blob => {
    try {
      if (!blob) return;
      const r = await fetch('/api/face-control', { method: 'POST', body: blob, headers: { 'Content-Type': 'image/jpeg' } });
      if (!r.ok) return;
      const d = await r.json();
      const now = performance.now();
      el.hudLatency.textContent = Math.round(now - t0);
      if (face.profile !== !!d.profileAngle) {
        face.profile = !!d.profileAngle;
        el.hudAngle.textContent = face.profile ? 'Perfil' : 'Frontal';
      }
      detCount++;
      if (now - lastDet >= 1000) { el.hudDetFps.textContent = Math.round(detCount * 1000 / (now - lastDet)); detCount = 0; lastDet = now; }

      face.target = d.faceDetected ? { x: d.box.normX, y: d.box.normY, w: d.box.normWidth, h: d.box.normHeight } : null;
      if (d.action === 'LinkedIn' || d.action === 'Interpol') {
        if (d.action !== face.last) { face.last = d.action; face.holdStart = performance.now(); }
      } else { face.last = 'Center'; face.holdStart = 0; }
      face.action = d.action;
    } catch { /* red inestable: se reintenta en el siguiente ciclo */ }
    finally {
      face.sending = false;
      if (face.active && !document.hidden) setTimeout(loop, Math.max(0, DET_INTERVAL_MS - (performance.now() - t0)));
    }
  }, 'image/jpeg', .6);
}

document.addEventListener('visibilitychange', () => {
  if (document.hidden) { stopTimer(); return; }
  frames = detCount = 0;
  lastFps = lastDet = performance.now();
  startDrawing();
  if (face.active) loop();
  // Al volver, el tiempo de la ronda sigue corriendo desde donde estaba
  if (state.canGuess) startTimer();
});

// ================= Escáner biométrico =================
el.scan.addEventListener('click', () => {
  if (!camReady) { toast('Activa la cámara para usar el escáner.'); return; }
  const c = Object.assign(document.createElement('canvas'), { width: el.video.videoWidth, height: el.video.videoHeight });
  const g = c.getContext('2d');
  g.setTransform(-1, 0, 0, 1, c.width, 0);
  g.drawImage(el.video, 0, 0);

  el.scan.disabled = true;
  const label = el.scan.textContent;
  el.scan.textContent = 'Analizando…';
  el.scanResult.innerHTML = '<div class="scan">Analizando ojos, sonrisa, simetría y luz…</div>';

  c.toBlob(async blob => {
    try {
      const r = await fetch('/api/biometric-scan', { method: 'POST', body: blob, headers: { 'Content-Type': 'image/jpeg' } });
      if (!r.ok) throw new Error(r.status);
      const d = await r.json();
      if (!d.success) {
        el.scanResult.innerHTML = `<div class="scan" style="color:var(--warn)">${escapeHtml(d.message || 'No se detectó tu rostro.')}</div>`;
        return;
      }
      const col = d.classification === 'LinkedIn' ? 'var(--li-dark)' : 'var(--in-dark)';
      const t = d.telemetry || {};
      const m = (name, v, c) =>
        `<div class="metric">${name}: <b class="num">${v}%</b><div class="meter"><i style="transform:scaleX(${Math.min(100, v) / 100});background:${c}"></i></div></div>`;
      el.scanResult.innerHTML = `
        <div class="scan">
          <img src="${d.imageData}" width="240" height="180" alt="Tu rostro analizado" style="border-color:${col}">
          <div><span class="tag" style="background:${col}">${escapeHtml(d.classification.toUpperCase())} · <span class="num">${d.probability}%</span></span></div>
          <p style="margin:.4rem 0"><b>${escapeHtml(d.description)}</b></p>
          <div class="metrics">
            ${m('Sonrisa', t.smileScore ?? 0, '#34d399')}${m('Simetría', t.symmetryScore ?? 0, '#3b8eea')}
            ${m('Luz', t.brightnessScore ?? 0, '#fbbf24')}${m('Nitidez', t.sharpnessScore ?? 0, '#a78bfa')}
          </div>
          <p class="hint" style="color:var(--ok)">Añadido a las rondas del juego.</p>
        </div>`;
      toast('Perfil añadido a las rondas.');
    } catch {
      el.scanResult.innerHTML = '<div class="scan" style="color:var(--in)">Error en el escaneo. Inténtalo de nuevo.</div>';
    } finally {
      el.scan.disabled = false;
      el.scan.textContent = label;
    }
  }, 'image/jpeg', .88);
});

// ================= Inicio =================
el.sound.setAttribute('aria-pressed', state.sound);
el.sound.textContent = state.sound ? '🔊' : '🔇';
setMode(state.mode);
setChoicesEnabled(false);
renderStats();
initCamera();
nextRound();
