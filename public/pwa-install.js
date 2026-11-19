(() => {
  const button = document.querySelector('[data-pwa-install]');
  const hint = document.querySelector('[data-pwa-install-hint]');
  if (!button) return;

  const ua = navigator.userAgent || '';
  const isIOS = /iPhone|iPad|iPod/i.test(ua) ||
    (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  const isMobile = isIOS || /Android|Mobile/i.test(ua);
  const isStandalone = () =>
    window.matchMedia('(display-mode: standalone)').matches ||
    window.matchMedia('(display-mode: fullscreen)').matches ||
    navigator.standalone === true;
  let installPrompt = null;
  let installed = isStandalone();

  const setHint = (message) => {
    if (hint) hint.textContent = message;
  };

  const hideInstall = () => {
    button.hidden = true;
    setHint('Fatal Shooter já está instalado. Abra o jogo pelo ícone do celular.');
  };

  const showInstall = () => {
    button.hidden = false;
    button.textContent = isIOS ? 'COMO INSTALAR NO IPHONE' : 'INSTALAR NO CELULAR';
  };

  async function checkInstalled() {
    if (installed) {
      hideInstall();
      return;
    }
    if (!isMobile) {
      button.hidden = true;
      return;
    }
    if (typeof navigator.getInstalledRelatedApps === 'function') {
      try {
        const apps = await navigator.getInstalledRelatedApps();
        if (apps.some((app) => app.platform === 'webapp')) {
          installed = true;
          hideInstall();
          return;
        }
      } catch (_) {
        // Alguns navegadores não permitem consultar apps relacionados.
      }
    }
    showInstall();
  }

  window.addEventListener('beforeinstallprompt', (event) => {
    if (!isMobile || installed) return;
    event.preventDefault();
    installPrompt = event;
    button.hidden = false;
    button.textContent = 'INSTALAR FATAL SHOOTER';
    setHint('Adicione o jogo à tela inicial para abrir em tela cheia.');
  });

  window.addEventListener('appinstalled', () => {
    installed = true;
    installPrompt = null;
    hideInstall();
  });

  button.addEventListener('click', async () => {
    if (installPrompt) {
      const promptEvent = installPrompt;
      installPrompt = null;
      await promptEvent.prompt();
      const choice = await promptEvent.userChoice;
      if (choice.outcome === 'accepted') {
        installed = true;
        hideInstall();
      } else {
        setHint('Se mudou de ideia, toque novamente em “Instalar Fatal Shooter”.');
      }
      return;
    }

    if (isIOS) {
      setHint('No iPhone ou iPad, toque em Compartilhar e escolha “Adicionar à Tela de Início”.');
    } else {
      setHint('Abra o menu ⋮ do navegador e escolha “Instalar app” ou “Adicionar à tela inicial”. Se não aparecer, abra o link no Chrome. Se já houver um ícone, abra o jogo por ele.');
    }
  });

  checkInstalled();
})();

