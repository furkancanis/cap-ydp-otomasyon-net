// Tema ve kenar menü tercihini sayfa çizilmeden uygular (yanıp sönmeyi önler). CSP uyumlu harici betik.
(function () {
  var d = document.documentElement;
  try {
    var t = localStorage.getItem('cap-theme');
    if (t === 'dark' || t === 'light') d.setAttribute('data-theme', t);
    if (localStorage.getItem('cap-sb') === 'mini') d.setAttribute('data-sb', 'mini');
  } catch (e) { /* depolama kapalı olabilir */ }
})();
