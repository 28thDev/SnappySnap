# Privacy and local data

SnappySnap stores screenshots and recordings on your computer. It has no accounts, cloud uploads, analytics or online crash reporting. Capturing, editing and browsing local media do not upload it. The application can contact GitHub to check for and download updates as described below.

- Default media directory: `%USERPROFILE%\Pictures\SnappySnap\YYYY-MM\`.
- Settings, history, caches and local logs: `%LOCALAPPDATA%\SnappySnap`.
- Per-user installation: `%LOCALAPPDATA%\Programs\SnappySnap`.
- Clipboard copying and dragging send content only to the destination you choose.
- Downloaded update packages, update status and installer logs: `%LOCALAPPDATA%\SnappySnap\Updates`. The automatic-check schedule is also stored in the local profile.
- Uninstall retains captures and application data, including update caches and logs. There is no automatic media/cache cleanup. Shelf's Delete command permanently deletes the selected media files; it does not use the Recycle Bin.

Recordings can contain screen content, system sound and microphone audio according to the selected controls. Review captures before sharing them. Diagnostic logs can include local paths and device/error details; redact them before attaching evidence to a public issue. Do not upload your whole settings/history directory.

## Update requests

Automatic update checks are enabled by default. While the app is running, the first eligible check occurs about 30 seconds after startup. A successful check, including when no release exists, schedules the next automatic check for at least 24 hours later. Network failures and rate limits delay retries; restarting the app preserves this schedule. Turn off **Settings → Updates → Check automatically**, then save, to disable automatic checks. **Check now** makes a request when you choose it.

Checks request release metadata from `api.github.com` for the official `28thDev/SnappySnap` repository. When a release exists, the app also retrieves its signed catalog and signature from GitHub release assets. These downloads can follow HTTPS redirects to GitHub's asset delivery infrastructure. Requests include a `SnappySnap/<version>` User-Agent; GitHub and its delivery providers receive ordinary connection information, such as the source IP address. SnappySnap does not attach captures, local filenames, history, logs, microphone recordings, account credentials or a persistent installation identifier to these requests.

The installer is downloaded only after you choose **Download**. Installation requires **Update and restart** and a verified package. A previously downloaded and verified installer can be used offline. Catalog signatures authenticate update packages; they are separate from Windows Authenticode publisher signatures.

Opening the repository or downloading Setup in a browser also uses that browser's network behavior and [GitHub's privacy policy](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
