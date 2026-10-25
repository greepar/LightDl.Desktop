document.documentElement.lang = chrome.i18n.getUILanguage();
document.title = chrome.i18n.getMessage("extensionName");
for (const element of document.querySelectorAll("[data-i18n]")) {
  const message = chrome.i18n.getMessage(element.dataset.i18n);
  if (message) element.textContent = message;
}
