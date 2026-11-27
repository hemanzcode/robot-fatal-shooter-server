mergeInto(LibraryManager.library, {
  RfsWsConnect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var s = window.__rfs = { ws: null, q: [], state: 0 };
    try {
      var ws = new WebSocket(url);
      s.ws = ws;
      ws.onopen = function () { s.state = 1; };
      ws.onmessage = function (e) { if (typeof e.data === 'string') s.q.push(e.data); };
      ws.onclose = function () { s.state = 2; };
      ws.onerror = function () { s.state = 2; };
    } catch (e) { s.state = 2; }
  },
  RfsWsState: function () { return window.__rfs ? window.__rfs.state : 0; },
  RfsWsSend: function (p) { var s = window.__rfs; if (s && s.ws && s.state === 1) s.ws.send(UTF8ToString(p)); },
  RfsWsRecv: function () {
    var s = window.__rfs;
    if (!s || !s.q.length) return 0;
    var m = s.q.shift();
    var n = lengthBytesUTF8(m) + 1;
    var b = _malloc(n);
    stringToUTF8(m, b, n);
    return b;
  },
  RfsFree: function (p) { _free(p); },
  RfsWsClose: function () { var s = window.__rfs; if (s && s.ws) { try { s.ws.close(); } catch (e) { } } window.__rfs = null; },
  RfsPrompt: function (titlePtr, valPtr) {
    var r = window.prompt(UTF8ToString(titlePtr), UTF8ToString(valPtr));
    if (r === null) return 0;
    var n = lengthBytesUTF8(r) + 1;
    var b = _malloc(n);
    stringToUTF8(r, b, n);
    return b;
  },
  // Convite: no celular abre o compartilhar do sistema (WhatsApp etc.); no PC copia o link.
  RfsShare: function (textPtr, urlPtr) {
    var text = UTF8ToString(textPtr), url = UTF8ToString(urlPtr);
    var copy = function () {
      if (navigator.clipboard && navigator.clipboard.writeText)
        navigator.clipboard.writeText(url).catch(function () { window.prompt('Copie o link da sala:', url); });
      else window.prompt('Copie o link da sala:', url);
    };
    var mobile = /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent);
    if (mobile && navigator.share) navigator.share({ title: 'Fatal Shooter', text: text, url: url }).catch(function () { });
    else copy();
  },
  // Foto da tela: baixa o PNG (vai para a pasta Downloads do PC ou do celular).
  RfsSaveFile: function (ptr, len, namePtr, mimePtr) {
    var name = UTF8ToString(namePtr), mime = UTF8ToString(mimePtr);
    var bytes = HEAPU8.slice(ptr, ptr + len);
    var blob = new Blob([bytes], { type: mime });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url; a.download = name; a.style.display = 'none';
    document.body.appendChild(a); a.click();
    setTimeout(function () { document.body.removeChild(a); URL.revokeObjectURL(url); }, 4000);
  },
  // Copia um texto (codigo PIX "copia e cola").
  RfsCopy: function (textPtr) {
    var t = UTF8ToString(textPtr);
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(t).catch(function () { window.prompt('Copie o codigo PIX:', t); });
    else window.prompt('Copie o codigo PIX:', t);
  },
  // Pixels por ponto da tela (o canvas e redimensionado no proximo quadro).
  RfsSetDpr: function (max) { Module.devicePixelRatio = Math.min(window.devicePixelRatio || 1, max); },
  // Som: pausa tudo quando a aba/app sai da tela (trocar de aba, minimizar, bloquear o celular) e volta ao retornar.
  RfsAudioSetup: function () {
    if (window.rfsAudioSetup) return;
    window.rfsAudioSetup = true;
    var sync = function () {
      if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) return;
      var ctx = WEBAudio.audioContext;
      if (document.hidden || window.rfsQuit) { if (ctx.state === 'running') ctx.suspend(); }
      else if (ctx.state === 'suspended') ctx.resume();
    };
    document.addEventListener('visibilitychange', sync);
    window.addEventListener('pagehide', sync);
    window.addEventListener('pageshow', sync);
  },
  // "Sair do jogo" no navegador: o site nao pode fechar a aba, entao corta o som e volta para a pagina inicial.
  RfsQuit: function () {
    window.rfsQuit = true;
    try { if (typeof WEBAudio !== 'undefined' && WEBAudio.audioContext) WEBAudio.audioContext.suspend(); } catch (e) {}
    window.location.href = '/';
  },
  // ---------------- Voz do time (WebRTC, um par por companheiro; sinalizacao passa pelo jogo) ----------------
  RfsVoiceStart: function () {
    if (window.__rv) return;
    var V = window.__rv = { me: 0, peers: {}, allowed: {}, out: [], mic: null, micState: 0, ice: [{ urls: 'stun:stun.l.google.com:19302' }, { urls: 'stun:stun1.l.google.com:19302' }], vol: 1, muted: {}, talk: false, ctx: null };
    V.send = function (to, obj) { V.out.push(to + '|' + btoa(unescape(encodeURIComponent(JSON.stringify(obj))))); };
    V.attachMic = function (P) {
      if (!V.mic || !P.pc) return;
      var tr = P.pc.getTransceivers().filter(function (t) { return t.receiver && t.receiver.track && t.receiver.track.kind === 'audio'; })[0];
      if (tr) { try { tr.direction = 'sendrecv'; tr.sender.replaceTrack(V.mic.getAudioTracks()[0]); } catch (e) { } }
    };
    V.close = function (id) {
      var P = V.peers[id]; if (!P) return;
      try { P.pc.close(); } catch (e) { }
      if (P.audio) { P.audio.srcObject = null; P.audio.remove(); }
      delete V.peers[id];
    };
    V.make = function (id) {
      V.close(id);
      var P = { id: id, pc: new RTCPeerConnection({ iceServers: V.ice }), pend: [], at: Date.now(), level: 0 };
      V.peers[id] = P;
      P.pc.onicecandidate = function (e) { if (e.candidate) V.send(id, { t: 'c', c: e.candidate.toJSON() }); };
      P.pc.ontrack = function (e) {
        var st = e.streams && e.streams[0] ? e.streams[0] : new MediaStream([e.track]);
        if (!P.audio) { P.audio = document.createElement('audio'); P.audio.autoplay = true; P.audio.playsInline = true; P.audio.style.display = 'none'; document.body.appendChild(P.audio); }
        P.audio.srcObject = st; P.audio.volume = V.muted[id] ? 0 : V.vol;
        var pl = P.audio.play(); if (pl && pl.catch) pl.catch(function () { });
        try {
          if (!V.ctx) V.ctx = new (window.AudioContext || window.webkitAudioContext)();
          var an = V.ctx.createAnalyser(); an.fftSize = 256;
          V.ctx.createMediaStreamSource(st).connect(an);
          P.an = an; P.buf = new Uint8Array(an.fftSize);
        } catch (e) { }
      };
      return P;
    };
    V.offer = function (id) {
      var P = V.make(id);
      P.pc.addTransceiver('audio', { direction: 'sendrecv' });
      V.attachMic(P);
      P.pc.createOffer().then(function (o) { return P.pc.setLocalDescription(o); })
        .then(function () { V.send(id, { t: 'o', sdp: P.pc.localDescription.sdp }); }).catch(function () { });
    };
    fetch('/voice/ice').then(function (r) { return r.json(); }).then(function (j) { if (j && j.iceServers && j.iceServers.length) V.ice = j.iceServers; }).catch(function () { });
    if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
      navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } }).then(function (st) {
        V.mic = st; V.micState = 1;
        st.getAudioTracks()[0].enabled = V.talk;
        for (var k in V.peers) V.attachMic(V.peers[k]);
      }).catch(function () { V.micState = 2; });
    } else V.micState = 2;
    // Quem nao conectou em 10 s tenta de novo (so o lado que oferece).
    V.timer = setInterval(function () {
      for (var k in V.allowed) {
        var id = +k, P = V.peers[id];
        if (V.me < id && (!P || (P.pc.connectionState !== 'connected' && Date.now() - P.at > 10000))) V.offer(id);
      }
    }, 2000);
  },
  RfsVoicePeers: function (me, csvPtr) {
    var V = window.__rv; if (!V) return;
    V.me = me;
    var ids = UTF8ToString(csvPtr).split(',').filter(function (x) { return x; }).map(Number), now = {};
    ids.forEach(function (id) { now[id] = 1; });
    for (var k in V.peers) if (!now[k]) V.close(+k);
    V.allowed = now;
    ids.forEach(function (id) { if (!V.peers[id] && me < id) V.offer(id); });
  },
  RfsVoiceSignal: function (from, b64Ptr) {
    var V = window.__rv; if (!V || !V.allowed[from]) return;
    var m; try { m = JSON.parse(decodeURIComponent(escape(atob(UTF8ToString(b64Ptr))))); } catch (e) { return; }
    var P = V.peers[from];
    if (m.t === 'o') {
      P = V.make(from);
      P.pc.setRemoteDescription({ type: 'offer', sdp: m.sdp }).then(function () {
        V.attachMic(P);
        P.pend.forEach(function (c) { P.pc.addIceCandidate(c).catch(function () { }); }); P.pend = [];
        return P.pc.createAnswer();
      }).then(function (a) { return P.pc.setLocalDescription(a); })
        .then(function () { V.send(from, { t: 'a', sdp: P.pc.localDescription.sdp }); }).catch(function () { });
    } else if (m.t === 'a' && P) {
      P.pc.setRemoteDescription({ type: 'answer', sdp: m.sdp }).then(function () {
        P.pend.forEach(function (c) { P.pc.addIceCandidate(c).catch(function () { }); }); P.pend = [];
      }).catch(function () { });
    } else if (m.t === 'c' && P) {
      if (P.pc.remoteDescription) P.pc.addIceCandidate(m.c).catch(function () { }); else P.pend.push(m.c);
    }
  },
  RfsVoicePoll: function () {
    var V = window.__rv; if (!V || !V.out.length) return 0;
    var m = V.out.shift(), n = lengthBytesUTF8(m) + 1, b = _malloc(n);
    stringToUTF8(m, b, n); return b;
  },
  RfsVoiceTalk: function (on) {
    var V = window.__rv; if (!V) return;
    V.talk = !!on;
    if (V.mic) V.mic.getAudioTracks()[0].enabled = V.talk;
    if (V.ctx && V.ctx.state === 'suspended') V.ctx.resume();
  },
  RfsVoiceMicState: function () { var V = window.__rv; return V ? V.micState : 0; },
  // Ids que estao falando agora (nivel de som acima do limite) e quantos estao conectados: "conectados;id,id".
  RfsVoiceStatus: function () {
    var V = window.__rv, r = '0;';
    if (V) {
      var on = 0, sp = [];
      for (var k in V.peers) {
        var P = V.peers[k];
        if (P.pc.connectionState === 'connected') on++;
        if (P.an && !V.muted[k]) {
          P.an.getByteTimeDomainData(P.buf);
          var mx = 0; for (var i = 0; i < P.buf.length; i++) { var d = Math.abs(P.buf[i] - 128); if (d > mx) mx = d; }
          P.level = Math.max(mx, P.level * 0.85);
          if (P.level > 10) sp.push(k);
        }
      }
      r = on + ';' + sp.join(',');
    }
    var n = lengthBytesUTF8(r) + 1, b = _malloc(n); stringToUTF8(r, b, n); return b;
  },
  RfsVoiceMute: function (id, muted) {
    var V = window.__rv; if (!V) return;
    V.muted[id] = !!muted; var P = V.peers[id]; if (P && P.audio) P.audio.volume = muted ? 0 : V.vol;
  },
  RfsVoiceVolume: function (v) {
    var V = window.__rv; if (!V) return;
    V.vol = v; for (var k in V.peers) { var P = V.peers[k]; if (P.audio) P.audio.volume = V.muted[k] ? 0 : v; }
  },
  RfsVoiceStop: function () {
    var V = window.__rv; if (!V) return;
    clearInterval(V.timer);
    for (var k in V.peers) V.close(+k);
    if (V.mic) V.mic.getTracks().forEach(function (t) { t.stop(); });
    try { if (V.ctx) V.ctx.close(); } catch (e) { }
    window.__rv = null;
  },
  // Limite de FPS no celular sem trocar o relogio do navegador: mede a taxa da tela e, se passar do limite,
  // pede ao laco principal para desenhar a cada N atualizacoes (120 Hz -> a cada 2 = 60 FPS). Reavalia a cada 2 s
  // (a tela pode alternar entre 60 e 120 Hz). Teste no PC: ?debug=1&capteste forca N=2.
  RfsFrameCap__deps: ['emscripten_set_main_loop_timing'],
  RfsFrameCap: function (maxFps) {
    if (window.__rfsCap) return;
    var C = window.__rfsCap = { d: [], hz: 60, n: 1 };
    var last = performance.now();
    var tick = function (t) { var dt = t - last; last = t; if (dt > 2 && dt < 100) { C.d.push(dt); if (C.d.length > 120) C.d.shift(); } requestAnimationFrame(tick); };
    requestAnimationFrame(tick);
    var force = /capteste/.test(location.search);
    setInterval(function () {
      if (C.d.length < 30) return;
      var s = C.d.slice().sort(function (a, b) { return a - b; });
      C.hz = 1000 / s[s.length >> 1];
      var n = force ? 2 : Math.max(1, Math.round(C.hz / maxFps));
      var cur = (typeof MainLoop !== 'undefined') ? MainLoop.timingValue : -1;
      if (n !== C.n || (typeof MainLoop !== 'undefined' && MainLoop.timingMode === 1 && cur !== n)) {
        C.n = n;
        try { _emscripten_set_main_loop_timing(1, n); } catch (e) { }
      }
    }, 2000);
  },
  // Painel de debug (?debug=1): 0 heap wasm MB, 1 heap JS MB (Chrome), 2 devicePixelRatio da tela, 3 dpr usado pela Unity.
  RfsDebugInfo: function (k) {
    if (k === 0) return (typeof HEAP8 !== 'undefined' ? HEAP8.length : (Module.HEAP8 ? Module.HEAP8.length : 0)) / 1048576;
    if (k === 1) return (performance && performance.memory) ? performance.memory.usedJSHeapSize / 1048576 : 0;
    if (k === 2) return window.devicePixelRatio || 1;
    if (k === 3) return Module.devicePixelRatio || window.devicePixelRatio || 1;
    if (k === 4) return window.__rfsCap ? window.__rfsCap.hz : 0;
    if (k === 5) return window.__rfsCap ? window.__rfsCap.n : 0;
    return 0;
  },
  RfsIsMobile: function () { return /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent) ? 1 : 0; }
});
