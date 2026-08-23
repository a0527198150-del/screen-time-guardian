# נעילת דפדפנים — איך זה עובד ומה הגבולות

## הבעיה

התוסף חוסם לפי חשבון Google. אבל תוסף חי בתוך דפדפן. מי שמתקין דפדפן חדש — התוסף לא שם, וכל החסימות ברמת החשבון נעלמות. התקנת Firefox לוקחת פחות מדקה.

## שלוש שכבות

### שכבה 1 — כפיית התוסף על Chrome ועל Edge

```powershell
.\Set-BrowserPolicies.ps1 `
  -ExtensionId '<מזהה Chrome>' `
  -EdgeExtensionId '<מזהה Edge>' `
  -UpdateUrl 'https://שרת-הפצה.example/chrome/update.xml' `
  -EdgeUpdateUrl 'https://שרת-הפצה.example/edge/update.xml'
```

הפקודה תקפה רק לאחר פרסום התוסף במקור הפצה מנוהל. תיקיית `Load unpacked` מקומית אינה מקור תקף למדיניות force-install.

### שלושה מצבי מדיניות תוספים

הפרמטר `-ExtensionPolicy` שולט במה שקורה לתוספים אחרים:

| מצב | התנהגות |
|---|---|
| `PermissionBased` (ברירת מחדל) | Guardian כפוי. תוספים אחרים מותרים, אבל הרשאות מסוכנות (proxy, VPN, webRequest, DNR, management, debugger, privacy) חסומות |
| `Allowlist` | Guardian כפוי. כל השאר חסומים. אפשר לאשר תוספים ספציפיים ב־`-AllowedExtensionIds` |
| `Strict` | Guardian כפוי. כל השאר חסומים |

```powershell
# מצב ברירת מחדל — תוספים אחרים מותרים, הרשאות מסוכנות חסומות
.\Set-BrowserPolicies.ps1 -ExtensionId ... -ExtensionPolicy PermissionBased

# אישור תוספים ספציפיים בלבד
.\Set-BrowserPolicies.ps1 -ExtensionId ... -ExtensionPolicy Allowlist `
    -AllowedExtensionIds cccccccccccccccccccccccccccccccc,dddddddddddddddddddddddddddddddd

# חסימה מלאה
.\Set-BrowserPolicies.ps1 -ExtensionId ... -ExtensionPolicy Strict
```

נכתב ל־`HKLM\SOFTWARE\Policies\Google\Chrome` ול־`HKLM\SOFTWARE\Policies\Microsoft\Edge`:

| מדיניות | תוצאה |
|---|---|
| `ExtensionInstallForcelist` | התוסף מותקן אוטומטית ולא ניתן להסרה |
| `ExtensionSettings` | תלוי במצב — `blocked_permissions`, wildcard block, או allowlist |
| מדיניות פרטית/אורח דינמית | השירות משנה אותן רק בזמן חסימה פעילה |
| `DeveloperToolsAvailability = 2` | DevTools חסום |
הכל תחת HKLM ⇒ חל על כל המשתמשים, והסרה דורשת הרשאת מנהל. בהסרה, הסקריפט מוחק רק ערכי Guardian שזוהו לפי מזהה/סימון בעלות.

**למה גלישה פרטית ומצב אורח עשויים להיחסם בזמן חסימה:** שניהם מסתירים איזה חשבון Google מחובר — וזה בדיוק מה שהתוסף צריך לראות כדי להחליט משהו.

במצב `PermissionBased`, הסקריפט מונע מתוספים לבקש הרשאות מסוכנות (proxy, VPN, יירוט תעבורה, ניהול תוספים אחרים, דיבוג, פרטיות) — אבל מילון, מצב לילה ומתרגם עובדים כרגיל.

### שכבה 2 — חסימת הפעלה לפי שם קובץ (IFEO)

נרשם `HKLM\...\Image File Execution Options\<שם>.exe` עם ערך `Debugger` שמצביע ל־`ScreenTimeGuardian.LaunchBlocker.exe`. Windows מפעיל את ה־stub במקום הדפדפן; הוא מציג הודעה בעברית ויוצא.

- עובד ב־Windows 10 Home (אין תלות ב־AppLocker)
- חל על כל המשתמשים
- תופס גם התקנה טרייה וגם גרסה ניידת
- הסרה דורשת הרשאת מנהל

**מגבלות בטיחות בקוד, ללא יוצא מן הכלל:**
- רק שמות שעוברים `BrowserIdentification.CanDenyByName` נרשמים. `explorer`, `svchost`, `cmd`, `msiexec` וכל תהליכי המערכת ברשימת NeverDeny.
- כל מפתח שנכתב מסומן ב־`STGOwned`. הקוד מוחק **רק** מפתחות עם הסימון שלו — IFEO משותף עם כלי פיתוח אחרים.
- **אם ה־stub חסר מהדיסק, לא נכתב כלום.** ערך `Debugger` שמצביע לקובץ לא קיים הופך תוכנית לבלתי ניתנת להפעלה לצמיתות.
- ההסרה מתבצעת בכיבוי תקין של השירות, ב־`Uninstall-Service.ps1` וב־`Emergency-Disable.ps1`.

### שכבה 3 — סורק דפדפנים מוסתרים

IFEO עובד לפי שם. `firefox.exe` ששמו שונה ל־`homework.exe` עובר.

הסורק הולך על התיקיות שמשתמש רגיל יכול לכתוב אליהן — Program Files, ProgramData, וכל פרופיל משתמש (Downloads, Desktop, Documents, AppData) — קורא את המטא־דאטה של כל `.exe`, ומזהה דפדפן לפי **יצרן ושם מוצר**, לא לפי שם הקובץ. מה שנמצא ולא מאושר מקבל חוק חומת אש. הדפדפן ייפתח — בלי אינטרנט.

בקרת עלות: תוצאות ב־cache לפי נתיב+גודל+זמן שינוי, עומק מוגבל ל־4, תקציב 8000 קבצים לסבב, ריצה על טיימר נפרד ולא על לולאת המדיניות.

**הזיהוי דורש שתי התאמות במקביל** — שם מוצר שנראה כמו דפדפן **וגם** יצרן מוכר או המילה "browser". התאמת מחרוזת בודדת היא בדיוק מה שגרם לבאג הקודם, שבו `msedgewebview2.exe` נחשב דפדפן.

## מה זה לא תופס

בכנות, ובלי לייפות:

1. **דפדפן נייד ששמו שונה וגם המטא־דאטה שלו נוקתה**, שרץ מדיסק חיצוני, בחלון שבין שתי סריקות. זה דורש מאמץ אמיתי — לא חצי דקה — אבל זה אפשרי.
2. **טלפון.** התוכנה הזו לא רואה את הטלפון.
3. **מחשב אחר.**
4. **הרשאת מנהל.** מי שיש לו אותה מפרק את הכל בשתי פקודות. זו הסיבה שהיא אצל אבא.

שכבה 1 ושכבה 2 מכסות את המקרה הריאלי. שכבה 3 מכסה את המתוחכם. השאר — תוכנת הסינון אמורה לחסום את ההורדה מלכתחילה.
