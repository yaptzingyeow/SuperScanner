# Translation review

The Malay, Chinese and Arabic text in the app was machine-written. Please ask one native speaker per language to review it (about 30–45 minutes each).

1. Open your language's CSV in Excel or Google Sheets (`ms-malay.csv`, `zh-chinese.csv`, `ar-arabic.csv`).
2. For each row, compare the English with the translation. Put **Y** in "OK?" if it reads naturally, or **N** and your wording in "Better wording".
3. Keep anything in `{curly braces}` exactly as it is (for example `{count}`). The app fills these in.
4. Keep it short. Most texts are button labels or one-line messages.
5. Send the file back. Changes go into `apps/web/src/app/core/i18n/lang/<code>.ts`.

Tip: start with keys beginning with `list.`, `nav.`, `upload.` and `ws.`, which are the screens people see most. `admin.` is only seen by administrators.
