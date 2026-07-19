const menuId = "lightdl-download";

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: menuId,
    title: "Download with LightDl",
    contexts: ["link", "video", "audio", "page"]
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  const url = info.linkUrl || info.srcUrl || info.pageUrl || tab?.url;
  if (url) openLightDl(url);
});

chrome.action.onClicked.addListener((tab) => {
  if (tab.url) openLightDl(tab.url);
});

function openLightDl(url) {
  chrome.tabs.create({ url: `lightdl://add?url=${encodeURIComponent(url)}` });
}
