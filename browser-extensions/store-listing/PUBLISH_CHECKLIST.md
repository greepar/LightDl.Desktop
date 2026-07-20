# Publishing Checklist

- [ ] Confirm version numbers in both manifests.
- [ ] Test the packaged ZIP files, not only unpacked source folders.
- [ ] Publish the privacy policy at a stable public URL.
- [ ] Prepare at least three screenshots showing setup, confirmation, and an active LightDl download.
- [ ] Prepare Chrome/Edge promotional images if the dashboards request them.
- [ ] Upload the Chromium ZIP to Chrome Web Store.
- [ ] Verify the Chrome Web Store extension ID. If it differs from `nafkhlacfpmamhmhdfbnfnkainjdaohi`, update `BrowserHostRegistrationService.ChromiumExtensionId` before releasing the desktop installer.
- [ ] Upload the Chromium ZIP to Microsoft Edge Add-ons and record its assigned extension ID.
- [ ] Add the Edge extension ID to the Chromium Native Messaging manifest before releasing the desktop installer.
- [ ] Upload the Firefox ZIP to AMO and keep the ID `automatic-integration@lightdl.io`.
- [ ] Copy the permission explanations from `CHROME_PRIVACY.md` into the store privacy forms.
- [ ] Copy `REVIEW_NOTES.md` into the reviewer instructions field.
- [ ] Confirm desktop installers include `LightDl.BrowserHost` and can register it on Windows, macOS, and Linux.
- [ ] Confirm the support issue tracker is monitored.
