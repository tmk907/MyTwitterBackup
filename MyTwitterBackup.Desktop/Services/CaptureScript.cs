namespace MyTwitterBackup.Desktop.Services;

/// <summary>
/// JavaScript injected into every x.com document (AddScriptToExecuteOnDocumentCreatedAsync, so it runs
/// before the page scripts). It:
/// 1. hooks XMLHttpRequest and fetch and forwards every GraphQL response body to the app
///    with window.chrome.webview.postMessage({ type: 'graphql', url, body });
/// 2. scans the rendered timeline (article elements) while the user scrolls and posts the ids of
///    visible posts: { type: 'dom', url, posts: [{ user, id }] }.
/// </summary>
public static class CaptureScript
{
    public const string Source = """
(() => {
  if (window.__myTwitterBackupInstalled) { return; }
  window.__myTwitterBackupInstalled = true;

  const post = (message) => {
    try { window.chrome.webview.postMessage(message); } catch (e) { }
  };
  // x.com/i/api/graphql/{id}/{Operation} (logged in) or api.x.com/graphql/{id}/{Operation} (logged out).
  const graphQlPattern = /\/graphql\/[^\/?]+\/[A-Za-z0-9_]+/;
  const isGraphQl = (url) => typeof url === 'string' && graphQlPattern.test(url);

  // XMLHttpRequest
  const originalOpen = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function (method, url) {
    try { this.__mtbUrl = String(url); } catch (e) { }
    return originalOpen.apply(this, arguments);
  };
  const originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.send = function () {
    if (isGraphQl(this.__mtbUrl)) {
      this.addEventListener('load', function () {
        try {
          if (this.status !== 200) { return; }
          let body = null;
          if (this.responseType === '' || this.responseType === 'text') { body = this.responseText; }
          else if (this.responseType === 'json') { body = JSON.stringify(this.response); }
          if (body) { post({ type: 'graphql', url: this.responseURL || this.__mtbUrl, body: body }); }
        } catch (e) { }
      });
    }
    return originalSend.apply(this, arguments);
  };

  // fetch
  const originalFetch = window.fetch;
  if (originalFetch) {
    window.fetch = function (input, init) {
      const url = typeof input === 'string' ? input : (input && input.url) || '';
      const promise = originalFetch.apply(this, arguments);
      if (isGraphQl(url)) {
        promise.then((response) => {
          if (!response.ok) { return; }
          response.clone().text()
            .then((body) => post({ type: 'graphql', url: response.url || url, body: body }))
            .catch(() => { });
        }).catch(() => { });
      }
      return promise;
    };
  }

  // Visible posts (DOM). The permalink of a post is the link that wraps its <time> element; when the
  // markup has no <time>, it is the first /{user}/status/{id} link of the article (header comes first,
  // quoted posts come after the post body).
  const statusPattern = /^\/([A-Za-z0-9_]{1,20})\/status\/(\d+)(?:\/(?:photo|video)\/\d+|\/analytics)?\/?$/;
  const parseLink = (link) => {
    const href = link && link.getAttribute('href');
    if (!href) { return null; }
    try { return statusPattern.exec(new URL(href, location.origin).pathname); } catch (e) { return null; }
  };
  let timer = null;
  const scan = () => {
    timer = null;
    const posts = [];
    document.querySelectorAll('article').forEach((article) => {
      const time = article.querySelector('a[href*="/status/"] time');
      let match = time ? parseLink(time.closest('a')) : null;
      if (!match) {
        const links = article.querySelectorAll('a[href*="/status/"]');
        for (let i = 0; i < links.length && !match; i++) { match = parseLink(links[i]); }
      }
      if (match) { posts.push({ user: match[1], id: match[2] }); }
    });
    if (posts.length > 0) { post({ type: 'dom', url: location.href, posts: posts }); }
  };
  const schedule = () => { if (!timer) { timer = setTimeout(scan, 700); } };
  const start = () => {
    new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
    window.addEventListener('scroll', schedule, { passive: true });
    schedule();
  };
  if (document.body) { start(); } else { document.addEventListener('DOMContentLoaded', start); }
})();
""";
}
