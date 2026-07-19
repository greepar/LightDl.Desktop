const menuId = "lightdl-download";

browser.runtime.onInstalled.addListener(() => {
  browser.contextMenus.create({
    id: menuId,
    title: "Download with LightDl",
    contexts: ["link", "video", "audio", "page"]
  });
});

browser.contextMenus.onClicked.addListener((info, tab) => {
  const url = info.linkUrl || info.srcUrl || info.pageUrl || tab?.url;
  if (url) openLightDl(url);
});

browser.action.onClicked.addListener((tab) => {
  if (tab.url) openLightDl(tab.url);
});

function openLightDl(url) {
  browser.tabs.create({ url: `lightdl://add?url=${encodeURIComponent(url)}` });
}
