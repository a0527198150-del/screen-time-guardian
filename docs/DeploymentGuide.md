# מדריך התקנה

## לפני הכל — הסרת הגרסה הישנה

**חובה.** הגרסה שמותקנת כרגע היא זו שגרמה ללולאת האתחולים. אבא צריך להריץ פעם אחת ב־PowerShell עם הרשאות מנהל:

```powershell
.\Uninstall-Service.ps1
```

זה עוצר ומוחק את השירות ומסיר כל חוק חומת אש שהוא יצר.

אם המחשב עדיין בלולאת אתחולים: להיכנס ל־Safe Mode (החזקת Shift בלחיצה על Restart ← Troubleshoot ← Advanced options ← Startup Settings ← Restart ← 4), ואז להריץ שם `sc delete ScreenTimeGuardian`.

## התקנה

1. להוריד את חבילת ה־build מ־GitHub Actions (workflow `package`).
2. לפרוס אותה ל־`C:\Program Files\ScreenTimeGuardian`.
3. PowerShell כמנהל:

```powershell
cd 'C:\Program Files\ScreenTimeGuardian\Policies'
.\Install-Service.ps1 -ServiceExecutable '..\Service\ScreenTimeGuardian.Service.exe' -StartAfterInstall
```

4. לרשום את ה־Native Host:

```powershell
.\Register-NativeHost.ps1
```

5. לפתוח את `ControlPanel\ScreenTimeGuardian.ControlPanel.exe` ולהגדיר סיסמת ניהול.

> הסיסמה נקבעת בהפעלה הראשונה, ומי שקובע אותה שולט בהגדרות. **אבא צריך לקבוע אותה, לא אתה** — אחרת אין למנגנון שום כוח מולך.

## התקנת התוסף

עד שתוגדר פריסה מנוהלת, טעינה ידנית:

1. Chrome ← `chrome://extensions` ← להפעיל Developer mode.
2. Load unpacked ← לבחור את תיקיית `Extension`.
3. להעתיק את מזהה התוסף שנוצר.
4. לערוך את `NativeHost\NativeHost.manifest.json` ולהכניס את המזהה ל־`allowed_origins`.
5. להריץ שוב את `Register-NativeHost.ps1`.

## בדיקה ראשונה — לפני שסומכים על זה

בצע בסדר הזה:

1. פתח את לוח הבקרה, לשונית **בטיחות ומצב**. ודא ש"אכיפה: פעילה". אם כתוב "מצב בטוח: כן" — זה תקין בהתקנה ראשונה אחרי קריסה. לחץ "בטל מצב בטוח".
2. צור כלל אחד בלבד, עם אפליקציה אחת לא חשובה (למשל Notepad++ או דפדפן משני).
3. שיטת אכיפה: **ניתוק מהאינטרנט בלבד**.
4. קבע חלון זמן של חמש דקות מהרגע הזה.
5. חכה 15 שניות, פתח את האפליקציה ובדוק שאין לה אינטרנט.
6. ודא שגלישה רגילה במחשב עובדת כרגיל, ושנטפרי מתפקד.
7. אתחל את המחשב פעם אחת וודא שהוא עולה תקין.

רק אחרי שכל השבעה עברו — הוסף כללים אמיתיים.

## אם משהו משתבש

הרץ מכל חלון PowerShell:

```powershell
.\Emergency-Disable.ps1
```

או פשוט צור קובץ ריק בשם `SAFEMODE` בתיקייה `C:\ProgramData\ScreenTimeGuardian`. כל האכיפה נעצרת תוך 15 שניות, בלי אתחול ובלי הסרה.
