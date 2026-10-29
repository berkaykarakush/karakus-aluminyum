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
      if (window.matchMedia("(max-width: 860px)").matches) {
        nav.classList.remove("is-open");
        toggle.setAttribute("aria-expanded", "false");
      }
    }));
  }
})();
