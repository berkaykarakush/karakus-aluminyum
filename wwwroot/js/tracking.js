// Çerez onayı ve izleme. Onay verilmeden Google, Meta ve panel raporu için hiçbir veri gönderilmez.
(() => {
  const element = document.getElementById("tracking-config");
  if (!element) return;
  const config = JSON.parse(element.textContent);
  const banner = document.getElementById("consent-banner");
  const KEY = "cookie_consent";

  const read = () => { try { return localStorage.getItem(KEY); } catch { return null; } };
  const write = (value) => { try { localStorage.setItem(KEY, value); } catch { /* depolama kapalı olabilir */ } };

  function session() {
    try {
      let current = JSON.parse(sessionStorage.getItem("ka_session") || "null");
      if (!current) {
        const params = new URLSearchParams(location.search);
        current = {
          id: crypto.randomUUID ? crypto.randomUUID().replaceAll("-", "") : `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`,
          referrer: document.referrer || null,
          utmSource: params.get("utm_source"), utmMedium: params.get("utm_medium"), utmCampaign: params.get("utm_campaign")
        };
        sessionStorage.setItem("ka_session", JSON.stringify(current));
      }
      return current;
    } catch { return null; }
  }

  function record(type) {
    if (!config.firstParty) return;
    const s = session();
    if (!s) return;
    const body = JSON.stringify({ type, path: location.pathname, referrer: s.referrer, utmSource: s.utmSource, utmMedium: s.utmMedium, utmCampaign: s.utmCampaign, sessionId: s.id });
    fetch("/t", { method: "POST", headers: { "Content-Type": "application/json" }, body, keepalive: true }).catch(() => {});
  }

  function loadScript(src) {
    const script = document.createElement("script");
    script.async = true;
    script.src = src;
    document.head.appendChild(script);
  }

  function startGoogle(id) {
    window.dataLayer = window.dataLayer || [];
    window.gtag = function () { window.dataLayer.push(arguments); };
    window.gtag("consent", "default", { ad_storage: "granted", analytics_storage: "granted", ad_user_data: "granted", ad_personalization: "granted" });
    window.gtag("js", new Date());
    window.gtag("config", id);
    loadScript(`https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`);
  }

  function startMeta(id) {
    const fbq = function () { fbq.callMethod ? fbq.callMethod.apply(fbq, arguments) : fbq.queue.push(arguments); };
    fbq.push = fbq; fbq.loaded = true; fbq.version = "2.0"; fbq.queue = [];
    window.fbq = window._fbq = fbq;
    loadScript("https://connect.facebook.net/en_US/fbevents.js");
    fbq("init", id);
    fbq("track", "PageView");
  }

  // Olayın üç hedefteki karşılığı
  const EVENTS = {
    whatsapp: { ga: "contact_whatsapp", meta: "Contact" },
    phone: { ga: "contact_phone", meta: "Contact" },
    email: { ga: "contact_email", meta: "Contact" },
    quote_cta: { ga: "quote_click", meta: "InitiateCheckout" },
    lead: { ga: "generate_lead", meta: "Lead" },
    view_service: { ga: "view_item", meta: "ViewContent" }
  };

  function track(type, label) {
    const event = EVENTS[type];
    if (!event) return;
    if (window.gtag) window.gtag("event", event.ga, label ? { item_name: label } : {});
    if (window.fbq) window.fbq("track", event.meta, label ? { content_name: label } : {});
    record(type);
  }

  let started = false;
  function start() {
    if (started) return;
    started = true;
    if (config.ga4) startGoogle(config.ga4);
    if (config.pixel) startMeta(config.pixel);
    record("pageview");
    if (config.event) track(config.event, config.eventLabel);

    document.addEventListener("click", (e) => {
      const link = e.target.closest?.("a[href]");
      if (!link) return;
      const href = link.getAttribute("href");
      if (href.startsWith("tel:")) track("phone");
      else if (href.startsWith("mailto:")) track("email");
      else if (href.includes("wa.me/")) track("whatsapp");
      else if (href.startsWith("/iletisim")) track("quote_cta");
    });
  }

  // Onay geri alınınca oluşmuş analiz çerezleri silinir, sayfa temiz yüklenir.
  function clearCookies() {
    document.cookie.split(";").map((c) => c.split("=")[0].trim()).filter((n) => /^(_ga|_gid|_gat|_fbp|_fbc)/.test(n)).forEach((name) => {
      const parts = location.hostname.split(".");
      [location.hostname, `.${parts.slice(-2).join(".")}`, ""].forEach((domain) => {
        document.cookie = `${name}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/${domain ? `; domain=${domain}` : ""}`;
      });
    });
    try { sessionStorage.removeItem("ka_session"); } catch { /* yok say */ }
  }

  function decide(value) {
    const wasGranted = read() === "granted";
    write(value);
    if (banner) banner.hidden = true;
    if (value === "granted") start();
    else { clearCookies(); if (wasGranted) location.reload(); }
  }

  banner?.querySelector("[data-consent-accept]")?.addEventListener("click", () => decide("granted"));
  banner?.querySelector("[data-consent-reject]")?.addEventListener("click", () => decide("denied"));
  document.querySelectorAll("[data-consent-open]").forEach((button) => button.addEventListener("click", () => { if (banner) banner.hidden = false; }));

  const decision = read();
  if (decision === "granted") start();
  else if (!decision && banner) banner.hidden = false;
})();
