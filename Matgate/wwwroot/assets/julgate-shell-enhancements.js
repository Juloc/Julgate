(() => {
  const isGerman = () => (document.documentElement.lang || '').toLowerCase().startsWith('de');
  const editLabel = () => isGerman() ? 'Bearbeiten' : 'Edit';

  const configureShellLink = (link, title) => {
    link.dataset.shellOpenTab = '1';
    link.dataset.shellTitle = title;
    link.title = title;
    link.setAttribute('aria-label', title);
  };

  const createEditLink = (href, compact) => {
    const title = editLabel();
    const link = document.createElement('a');
    link.href = href;
    link.className = compact
      ? 'button toolbar-button toolbar-icon-button julgate-server-edit-action'
      : 'button julgate-server-edit-action';
    configureShellLink(link, title);
    link.innerHTML = compact
      ? '<span aria-hidden="true">✎</span>'
      : `<span aria-hidden="true">✎</span><span>${title}</span>`;
    return link;
  };

  const addCardEditActions = () => {
    // Owners already receive a server-rendered Edit action. The administration link
    // identifies administrators and global server managers who may edit global entries.
    if (!document.querySelector('a[href="/admin/servers"]')) return;

    document.querySelectorAll('.connection-choice').forEach(card => {
      if (card.querySelector('.julgate-server-edit-action')) return;
      const launchButton = card.querySelector('[data-server-id]');
      const actions = card.querySelector('.connection-choice-actions');
      const serverId = launchButton?.getAttribute('data-server-id');
      if (!actions || !serverId) return;
      actions.prepend(createEditLink(`/admin/servers/${encodeURIComponent(serverId)}`, false));
    });
  };

  const addTableEditActions = () => {
    document.querySelectorAll('table tbody tr').forEach(row => {
      if (row.querySelector('.julgate-server-edit-action')) return;
      const detailLink = Array.from(row.querySelectorAll('a[href^="/admin/servers/"]'))
        .find(link => /^\/admin\/servers\/[0-9a-f-]{36}$/i.test(link.getAttribute('href') || ''));
      if (!detailLink) return;

      const actionCell = row.lastElementChild;
      if (!(actionCell instanceof HTMLElement)) return;
      actionCell.append(createEditLink(detailLink.getAttribute('href'), true));
    });
  };

  const apply = () => {
    addCardEditActions();
    addTableEditActions();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', apply, { once: true });
  } else {
    apply();
  }

  const observer = new MutationObserver(apply);
  observer.observe(document.documentElement, { subtree: true, childList: true });
  window.setTimeout(() => observer.disconnect(), 5000);
})();
