// ÇAP / YDP Takip — küçük arayüz yardımcıları (satır içi betik kullanılmaz; CSP uyumlu)
(function () {
  'use strict';

  // Onay isteyen formlar: <form data-confirm="...">
  document.addEventListener('submit', function (e) {
    var f = e.target;
    var msg = f.getAttribute('data-confirm');
    if (msg && !window.confirm(msg)) {
      e.preventDefault();
      return;
    }
    // çift gönderimi engelle
    var btns = f.querySelectorAll('button[type=submit]');
    setTimeout(function () { btns.forEach(function (b) { b.disabled = true; b.classList.add('is-loading'); b.setAttribute('aria-busy', 'true'); }); }, 0);
  });

  // Açılır işlem kutularından yalnızca biri açık kalsın
  document.addEventListener('toggle', function (e) {
    var d = e.target;
    if (d.tagName === 'DETAILS' && d.classList.contains('pop') && d.open) {
      document.querySelectorAll('details.pop[open]').forEach(function (o) { if (o !== d) o.open = false; });
      var first = d.querySelector('textarea, input:not([type=hidden]), select');
      if (first) first.focus();
    }
  }, true);
  document.addEventListener('click', function (e) {
    if (!e.target.closest('details.pop')) {
      document.querySelectorAll('details.pop[open]').forEach(function (o) { o.open = false; });
    }
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') document.querySelectorAll('details.pop[open]').forEach(function (o) { o.open = false; });
  });

  // Mesaj penceresini en alta kaydır
  document.querySelectorAll('.chat').forEach(function (c) { c.scrollTop = c.scrollHeight; });

  // Toplu seçim kutuları: <input type=checkbox data-check-all="name">
  document.querySelectorAll('[data-check-all]').forEach(function (box) {
    box.addEventListener('change', function () {
      var n = box.getAttribute('data-check-all');
      document.querySelectorAll('input[type=checkbox][name="' + n + '"]').forEach(function (c) { c.checked = box.checked; });
    });
  });

  // Ders durumu "geçti/kaldı" değilse not alanını pasifleştir
  document.querySelectorAll('select[data-status-for]').forEach(function (sel) {
    var target = document.getElementById(sel.getAttribute('data-status-for'));
    function sync() {
      if (!target) return;
      var needs = ['gecti', 'kaldi', 'muaf'].indexOf(sel.value) >= 0;
      target.disabled = !needs;
      if (!needs) target.value = '';
    }
    sel.addEventListener('change', sync);
    sync();
  });

  // Dosya boyutu ön kontrolü
  document.querySelectorAll('input[type=file][data-max-mb]').forEach(function (inp) {
    inp.addEventListener('change', function () {
      var max = parseFloat(inp.getAttribute('data-max-mb')) * 1024 * 1024;
      if (inp.files[0] && inp.files[0].size > max) {
        alert('Dosya en fazla ' + inp.getAttribute('data-max-mb') + ' MB olabilir.');
        inp.value = '';
      }
    });
  });

  var root = document.documentElement;
  var body = document.body;
  function store(k, v) { try { if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (e) { /* yoksay */ } }

  // ---------------- Tema (açık / koyu) ----------------
  function isDark() {
    var t = root.getAttribute('data-theme');
    if (t) return t === 'dark';
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  }
  document.querySelectorAll('[data-theme-toggle]').forEach(function (b) {
    b.setAttribute('aria-pressed', isDark() ? 'true' : 'false');
    b.addEventListener('click', function () {
      var next = isDark() ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      store('cap-theme', next);
      b.setAttribute('aria-pressed', next === 'dark' ? 'true' : 'false');
    });
  });

  // ---------------- Kenar menü: masaüstünde daralt, mobilde çekmece ----------------
  var sbToggle = document.querySelector('[data-sb-toggle]');
  var scrim = document.querySelector('.scrim');
  var mq = window.matchMedia('(max-width: 1023px)');
  function setDrawer(open) {
    body.classList.toggle('sb-open', open);
    if (scrim) scrim.hidden = !open;
    if (sbToggle) sbToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open) {
      var first = document.querySelector('.sb .sb-link');
      if (first) first.focus();
    }
  }
  if (sbToggle) {
    sbToggle.setAttribute('aria-expanded', mq.matches ? 'false' : (root.getAttribute('data-sb') === 'mini' ? 'false' : 'true'));
    sbToggle.addEventListener('click', function () {
      if (mq.matches) {
        setDrawer(!body.classList.contains('sb-open'));
      } else {
        var mini = root.getAttribute('data-sb') !== 'mini';
        if (mini) root.setAttribute('data-sb', 'mini'); else root.removeAttribute('data-sb');
        store('cap-sb', mini ? 'mini' : null);
        sbToggle.setAttribute('aria-expanded', mini ? 'false' : 'true');
      }
    });
  }
  document.querySelectorAll('[data-sb-close]').forEach(function (el) {
    el.addEventListener('click', function () { setDrawer(false); if (sbToggle) sbToggle.focus(); });
  });
  if (mq.addEventListener) mq.addEventListener('change', function () { setDrawer(false); });

  // ---------------- Kullanıcı menüsü ----------------
  function closeMenus(except) {
    document.querySelectorAll('.menu.open').forEach(function (m) {
      if (m !== except) { m.classList.remove('open'); var b = m.querySelector('[data-menu]'); if (b) b.setAttribute('aria-expanded', 'false'); }
    });
  }
  document.querySelectorAll('[data-menu]').forEach(function (btn) {
    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      var m = btn.parentElement;
      var open = !m.classList.contains('open');
      closeMenus(m);
      m.classList.toggle('open', open);
      btn.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (open) { var it = m.querySelector('[role=menuitem]'); if (it) it.focus(); }
    });
  });
  document.addEventListener('click', function (e) { if (!e.target.closest('.menu')) closeMenus(null); });
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    var openMenu = document.querySelector('.menu.open');
    if (openMenu) { closeMenus(null); var b = openMenu.querySelector('[data-menu]'); if (b) b.focus(); }
    if (body.classList.contains('sb-open')) { setDrawer(false); if (sbToggle) sbToggle.focus(); }
  });

  // ---------------- Bildirim baloncukları (5 sn; hata/uyarı kullanıcı kapatana kadar kalır) ----------------
  function hideToast(t) {
    t.classList.add('hide');
    setTimeout(function () { if (t.parentNode) t.parentNode.removeChild(t); }, 220);
  }
  document.querySelectorAll('.toast').forEach(function (t) {
    var x = t.querySelector('[data-toast-close]');
    if (x) x.addEventListener('click', function () { hideToast(t); });
    if (!t.classList.contains('error') && !t.classList.contains('warn')) {
      var timer = setTimeout(function () { hideToast(t); }, 5000);
      t.addEventListener('mouseenter', function () { clearTimeout(timer); });
      t.addEventListener('focusin', function () { clearTimeout(timer); });
    }
  });

  // ---------------- Giriş formu ----------------
  document.querySelectorAll('[data-pw-toggle]').forEach(function (b) {
    b.addEventListener('click', function () {
      var inp = document.getElementById(b.getAttribute('aria-controls'));
      if (!inp) return;
      var show = inp.type === 'password';
      inp.type = show ? 'text' : 'password';
      b.setAttribute('aria-pressed', show ? 'true' : 'false');
      b.setAttribute('aria-label', show ? 'Şifreyi gizle' : 'Şifreyi göster');
    });
  });
  document.querySelectorAll('[data-fill-user]').forEach(function (b) {
    b.addEventListener('click', function () {
      var u = document.getElementById('username');
      var p = document.getElementById('password');
      if (!u || !p) return;
      u.value = b.getAttribute('data-fill-user');
      p.value = b.getAttribute('data-fill-pass');
      document.querySelectorAll('.ta[aria-pressed=true]').forEach(function (o) { o.setAttribute('aria-pressed', 'false'); });
      b.setAttribute('aria-pressed', 'true');
      validateField(u); validateField(p);
    });
  });
  function validateField(inp) {
    var err = document.getElementById(inp.id + '-err');
    var bad = !inp.value.trim();
    inp.setAttribute('aria-invalid', bad ? 'true' : 'false');
    if (err) err.hidden = !bad;
    return !bad;
  }
  document.querySelectorAll('form[data-login]').forEach(function (f) {
    var fields = f.querySelectorAll('input[required]');
    fields.forEach(function (inp) {
      inp.addEventListener('blur', function () { if (inp.getAttribute('aria-invalid') !== null || inp.value) validateField(inp); });
      inp.addEventListener('input', function () { if (inp.getAttribute('aria-invalid') === 'true') validateField(inp); });
    });
    f.addEventListener('submit', function (e) {
      var firstBad = null;
      fields.forEach(function (inp) { if (!validateField(inp) && !firstBad) firstBad = inp; });
      if (firstBad) {
        e.preventDefault();
        e.stopImmediatePropagation();
        firstBad.focus();
        return;
      }
      var btn = f.querySelector('button[type=submit]');
      if (btn) btn.classList.add('is-loading');
    }, true);
  });
  // Geri tuşuyla dönülünce (bfcache) kilitli kalan butonları aç
  window.addEventListener('pageshow', function (e) {
    if (e.persisted) document.querySelectorAll('button.is-loading').forEach(function (b) { b.disabled = false; b.classList.remove('is-loading'); b.removeAttribute('aria-busy'); });
  });
})();
