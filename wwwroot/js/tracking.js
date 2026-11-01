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

  // Google raporlarındaki "İçerik grubu" boyutu.
  function contentGroup() {
    const path = location.pathname;
    if (path === "/") return "Ana sayfa";
    const groups = { urunler: "Ürünler", projeler: "Projeler", blog: "Blog", galeri: "Galeri", kurumsal: "Kurumsal", iletisim: "İletişim", "gizlilik-politikasi": "Yasal", "kvkk-aydinlatma-metni": "Yasal" };
    return groups[path.split("/")[1]] || "Diğer";
  }

  function startGoogle(id) {
    window.dataLayer = window.dataLayer || [];
    window.gtag = function () { window.dataLayer.push(arguments); };
    // Çerez metni reklam ölçümünü söyler; kişiselleştirilmiş reklam izni verilmez.
    window.gtag("consent", "default", { ad_storage: "granted", analytics_storage: "granted", ad_user_data: "granted", ad_personalization: "denied" });
    window.gtag("js", new Date());
    window.gtag("config", id, { content_group: contentGroup() });
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

  // Olayın Google ve Meta karşılığı. Google parametrelerine ad, telefon gibi kişisel veri yazılmaz.
  const EVENTS = {
    whatsapp: { ga: "contact_whatsapp", meta: "Contact" },
    phone: { ga: "contact_phone", meta: "Contact" },
    email: { ga: "contact_email", meta: "Contact" },
    quote_cta: { ga: "quote_click", meta: "InitiateCheckout" },
    lead: { ga: "generate_lead", meta: "Lead" },
    view_service: { ga: "view_item", meta: "ViewContent" },
    view_project: { ga: "view_project", meta: "ViewContent" },
    view_article: { ga: "view_article", meta: "ViewContent" },
    contact_form_start: { ga: "contact_form_start" },
    contact_form_error: { ga: "contact_form_error" },
    select_content: { ga: "select_content" },
    faq_open: { ga: "faq_open" },
    scroll_depth: { ga: "scroll_depth" }
  };
  // Panel raporunun tanıdığı olaylar; diğerleri yalnızca Google'a gider.
  const FIRST_PARTY = new Set(["whatsapp", "phone", "email", "quote_cta", "lead", "view_service"]);

  // Google metin parametrelerini 100 karakterde keser; boş değer gönderilmez.
  function clean(params) {
    return Object.fromEntries(Object.entries(params).filter(([, value]) => value !== null && value !== undefined && value !== "").map(([key, value]) => [key, typeof value === "string" ? value.slice(0, 100) : value]));
  }

  function track(type, params = {}) {
    const event = EVENTS[type];
    if (!event) return;
    const data = clean(params);
    if (window.gtag) window.gtag("event", event.ga, data);
    if (window.fbq && event.meta) window.fbq("track", event.meta, data.item_name ? { content_name: data.item_name } : {});
    if (FIRST_PARTY.has(type)) record(type);
  }

  // Sunucunun sayfaya yazdığı olay: görüntülenen içerik, gönderilen veya hatalı form.
  function pageEvent() {
    const label = config.eventLabel;
    const id = config.eventId;
    switch (config.event) {
      case "view_service": return { item_name: label, item_id: id, items: [{ item_id: id, item_name: label, item_category: "Hizmet" }] };
      case "view_project": case "view_article": return { item_name: label, item_id: id };
      case "lead": return { form_name: "iletisim", item_name: label };
      case "contact_form_error": return { form_name: "iletisim", error_fields: label };
      default: return {};
    }
  }

  function linkLocation(link) {
    if (link.closest(".floating-contact")) return "sabit_buton";
    if (link.closest("header")) return "ust_menu";
    if (link.closest("footer")) return "alt_menu";
    return "sayfa";
  }

  function trackLink(link) {
    const href = link.getAttribute("href");
    const common = {
      link_location: linkLocation(link), link_text: link.textContent.replace(/[↗\s]+/g, " ").trim(),
      item_name: config.event === "view_service" ? config.eventLabel : null
    };
    if (href.startsWith("tel:")) return track("phone", common);
    // Cloudflare e-posta adreslerini gizlerken bağlantıyı bu adrese çevirebilir.
    if (href.startsWith("mailto:") || href.includes("/cdn-cgi/l/email-protection")) return track("email", common);
    if (href.includes("wa.me/")) return track("whatsapp", common);
    if (href.startsWith("/iletisim")) return track("quote_cta", common);
    const detail = href.match(/^\/(urunler|projeler|blog)\/([^/?#]+)/);
    if (detail) track("select_content", { content_type: { urunler: "hizmet", projeler: "proje", blog: "blog" }[detail[1]], content_id: detail[2], link_location: common.link_location });
  }

  function trackScroll() {
    const steps = [25, 50, 75];
    const onScroll = () => {
      const height = document.documentElement.scrollHeight - window.innerHeight;
      if (height <= 0) return;
      const percent = (window.scrollY / height) * 100;
      while (steps.length && percent >= steps[0]) track("scroll_depth", { percent_scrolled: steps.shift() });
      if (!steps.length) window.removeEventListener("scroll", onScroll);
    };
    window.addEventListener("scroll", onScroll, { passive: true });
  }

  let started = false;
  function start() {
    if (started) return;
    started = true;
    if (config.ga4) startGoogle(config.ga4);
    if (config.pixel) startMeta(config.pixel);
    record("pageview");
    if (config.event) track(config.event, pageEvent());

    document.addEventListener("click", (e) => {
      const link = e.target.closest?.("a[href]");
      if (link) trackLink(link);
    });

    // Formu doldurmaya başlayıp göndermeyenleri görmek için sayfa başına bir kez sayılır.
    let formStarted = false;
    document.addEventListener("focusin", (e) => {
      if (formStarted || !e.target.closest?.('form[action="/iletisim"]')) return;
      formStarted = true;
      track("contact_form_start", { form_name: "iletisim" });
    });

    // toggle olayı kabarcıklanmaz; ilk açık gelen soru sayılmaz.
    document.querySelectorAll("details.faq-item").forEach((item) => item.addEventListener("toggle", () => {
      if (item.open) track("faq_open", { faq_question: item.querySelector("summary")?.textContent.trim() });
    }));

    trackScroll();
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
