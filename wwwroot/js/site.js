(() => {
  const toggle = document.querySelector(".menu-toggle");
  const nav = document.querySelector("#primary-nav");
  if (toggle && nav) {
    toggle.addEventListener("click", () => {
      const open = toggle.getAttribute("aria-expanded") === "true";
      toggle.setAttribute("aria-expanded", String(!open));
      nav.classList.toggle("is-open", !open);
    });
    nav.querySelectorAll("a").forEach(link => link.addEventListener("click", () => {
      if (window.matchMedia("(max-width: 960px)").matches) {
        nav.classList.remove("is-open");
        toggle.setAttribute("aria-expanded", "false");
      }
    }));
  }

  const header = document.querySelector(".site-header");
  if (header) {
    const onScroll = () => header.classList.toggle("is-scrolled", window.scrollY > 40);
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();
  }

  const map = document.querySelector("[data-map-src]");
  if (map) {
    const loadMap = () => {
      if (map.querySelector("iframe")) return;
      const frame = document.createElement("iframe");
      frame.src = map.dataset.mapSrc;
      frame.title = map.dataset.mapTitle;
      frame.allowFullscreen = true;
      frame.referrerPolicy = "no-referrer-when-downgrade";
      map.replaceChildren(frame);
    };
    map.querySelector("button").addEventListener("click", loadMap);
    document.querySelector("[data-consent-accept]")?.addEventListener("click", loadMap);
    try { if (localStorage.getItem("cookie_consent") === "granted") loadMap(); } catch { /* depolama kapalı olabilir */ }
  }

  const targets = document.querySelectorAll(".section-heading, .service-card, .mosaic-card, .project-card, .split__visual, .split__copy, .step, .faq-layout, .cta-band, .gallery-item, .detail-layout__main, .side-card, .about-lead, .duo-figure img, .principle, .expertise-row");
  if ("IntersectionObserver" in window && !window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    const observer = new IntersectionObserver(entries => entries.forEach(entry => {
      if (!entry.isIntersecting) return;
      entry.target.classList.add("is-in");
      observer.unobserve(entry.target);
    }), { rootMargin: "0px 0px -8% 0px" });
    targets.forEach(target => {
      target.classList.add("reveal");
      observer.observe(target);
    });
  }
})();
