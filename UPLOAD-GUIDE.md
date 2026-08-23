# איך להעלות את הגרסה הזו ל־GitHub

מדריך צעד־אחר־צעד דרך הממשק של GitHub, בלי Git מקומי.

---

## שלב 0 — הסר את הגרסה הישנה מהמחשב (עשה את זה קודם)

הגרסה שמותקנת עכשיו היא זו שגרמה ללולאת האתחולים. אל תשאיר אותה שם.

אבא, ב־PowerShell עם הרשאות מנהל:

```powershell
sc.exe stop ScreenTimeGuardian
sc.exe delete ScreenTimeGuardian
Get-NetFirewallRule -Name 'STG-*' -EA SilentlyContinue | Remove-NetFirewallRule
```

זה לא תלוי בכלום מהצעדים הבאים, וזה הכי דחוף.

---

## שלב 1 — חלץ את ה־ZIP

הורד את `screen-time-guardian-v0.4.3.zip`, לחיצה ימנית ← **Extract All**.

תקבל תיקייה עם: `src`, `extension`, `policies`, `docs`, `tools`, `.github`, ו־4 קבצים בשורש.

> **שים לב:** ב־Windows תיקיות שמתחילות בנקודה כמו `.github` מוסתרות. ב־File Explorer: לשונית **View** ← סמן **Hidden items**.

---

## שלב 2 — צור ענף חדש (אל תעלה ישר ל־main)

זה החלק הכי חשוב במדריך. אם תעלה ישר ל־main ומשהו לא מתקמפל, אתה תקוע עם main שבור.

1. היכנס ל־`github.com/a0527198150-del/screen-time-guardian`
2. לחץ על התפריט הנפתח שכתוב **main** (למעלה משמאל)
3. בשדה הכתוב `Find or create a branch...` הקלד: **`v0.4.3`**
4. לחץ **Create branch: v0.4.2 from main**

עכשיו אתה על הענף `v0.4.3`. ודא שכתוב שם בתפריט לפני שאתה ממשיך.

---

## שלב 3 — מחק 6 קבצים ישנים

**זה קריטי.** העלאת קבצים ב־GitHub רק מוסיפה ודורסת — היא **לא מוחקת**. הקבצים האלה כוללים את הקוד שהפיל לך את המחשב, וגם אם הם לא ירוצו הם ישברו את הקומפילציה.

לכל אחד מששת הקבצים:

1. נווט אליו בממשק
2. לחץ על שם הקובץ כדי לפתוח אותו
3. לחץ על סמל **פח האשפה** (מימין למעלה, ליד כפתור Edit)
4. גלול למטה ← **Commit changes** ← ודא שכתוב `v0.4.3` ← **Commit changes**

הקבצים:

```
src/Guardian.Service/PortableBrowserEnforcer.cs      ← הגורם לקריסה
src/Guardian.Service/ProcessBlocker.cs               ← הגורם לקריסה
src/Guardian.Service/BrowserProcessDetector.cs
src/Guardian.Contracts/BrowserApprovalPolicy.cs
src/Guardian.ControlPanel/BrowserInventory.cs
src/Guardian.Service/UpdateCoordinator.cs
src/Guardian.Service/ProcessCloser.cs
src/Guardian.Service/SessionUserResolver.cs
```

> שני האחרונים נוספו בגרסה 0.4.3: סגירת תהליכים בכוח הוסרה מהתוכנה לחלוטין.

---

## שלב 4 — העלה את הקבצים החדשים

1. ודא שאתה על הענף `v0.4.3`
2. **Add file** ← **Upload files**
3. פתח את התיקייה שחילצת בשלב 1
4. סמן הכל (`Ctrl+A`) **וגרור לתוך החלון של הדפדפן**

GitHub שומר על מבנה התיקיות בגרירה. אתה אמור לראות רשימה של כ־60 קבצים.

5. גלול למטה, בשדה התיאור כתוב: `v0.4.3 - safety envelope, browser lockdown, agent`
6. ודא ש**Commit directly to the v0.4.2 branch** מסומן
7. **Commit changes**

> אם הדפדפן נתקע על העלאה של הכל בבת אחת — העלה בשלוש מנות: קודם `src`, אחר כך `extension` + `policies` + `docs` + `tools`, ולבסוף `.github` והקבצים שבשורש.

---

## שלב 5 — הרץ את הבנייה

ה־workflow מוגדר לרוץ רק על `main`, אז על ענף צריך להריץ ידנית:

1. לשונית **Actions**
2. בצד ימין בחר **build**
3. **Run workflow** ← בתפריט הנפתח בחר **`v0.4.3`** ← **Run workflow**
4. רענן. תוך כדקה יופיע ריצה חדשה — לחץ עליה

**✅ עיגול ירוק** ← עבור לשלב 6.

**❌ X אדום** ← לחץ על השלב האדום, גלול לשורה הראשונה שמתחילה ב־`error`, והעתק אותה אליי. אל תנחש ואל תתקן לבד — קרוב לוודאי שזה `System.IO.Pipes.AccessControl`, והפתרון המדויק כתוב ב־`docs/BuildNotes.md`.

---

## שלב 6 — מזג ל־main

רק אחרי שהבנייה ירוקה.

1. לשונית **Pull requests** ← **New pull request**
2. `base: main`  ←  `compare: v0.4.2`
3. **Create pull request** ← **Merge pull request** ← **Confirm merge**

---

## שלב 7 — צור חבילת התקנה

1. **Actions** ← **package** ← **Run workflow** ← בחר **main** ← **Run workflow**
2. כשמסתיים, לחץ על הריצה וגלול למטה ל־**Artifacts**
3. הורד את `ScreenTimeGuardian.zip`

---

## שלב 8 — התקן במחשב

אחרי הכל, קרא את **`docs/DeploymentGuide.md`** שבתוך החבילה. שם יש רשימת בדיקה של 7 שלבים.

**סדר ההפעלה — אל תדלג ואל תשנה:**

| # | מה | למה בסדר הזה |
|---|---|---|
| 1 | התקן שירות + לוח בקרה | הבסיס |
| 2 | כלל אחד, אפליקציה לא חשובה, **ניתוק מהאינטרנט בלבד**, 5 דקות | הכי בטוח שיש |
| 3 | **אתחל את המחשב וודא שהוא עולה** | זו הבדיקה שנכשלה בפעם הקודמת |
| 4 | תוסף + `Set-BrowserPolicies.ps1` | שכבה 1, הכי משתלמת |
| 5 | סוכן ההתראות (`Install-Agent.ps1`) | בטוח, רק מציג חלונות |
| 6 | סורק דפדפנים מוסתרים | רק מוסיף חוקי חומת אש |
| 7 | **חסימת הפעלה (IFEO) — אחרון** | הפעולה היחידה שיכולה להשאיר תוכנית שבורה. רק אחרי שהכל יציב |

**אל תפעיל את שלב 7 באותו יום של שלב 1.** תן לזה כמה ימים לרוץ.

---

## אם משהו משתבש בשלב כלשהו

```powershell
.\Emergency-Disable.ps1
```

או פשוט צור קובץ ריק בשם `SAFEMODE` בתיקייה `C:\ProgramData\ScreenTimeGuardian`. כל האכיפה נעצרת תוך 15 שניות — בלי אתחול, בלי הסרה.

אם המחשב לא עולה בכלל: Safe Mode (החזק Shift בלחיצה על Restart ← Troubleshoot ← Advanced options ← Startup Settings ← Restart ← מקש 4), ואז `sc delete ScreenTimeGuardian`.
