# מסירת גרסה 0.4.4

הקוד כבר נמצא ב־`main`. אין להעלות קובצי ZIP לתוך הריפו ואין לבצע מחיקות ידניות דרך GitHub. שינויים נמסרים באמצעות commit ו־push, וה־workflow בודק אותם אוטומטית.

## בדיקת build

1. ודא שהעץ נקי ושכל השינויים הרלוונטיים נשמרו.
2. דחוף ל־`main`.
3. פתח את GitHub Actions והמתן ל־workflow `build`.
4. המשך רק כאשר שלבי `Restore` ו־`Build` ירוקים.

## יצירת חבילת התקנה

1. פתח את GitHub Actions ובחר `package`.
2. הפעל את ה־workflow על `main` או צור תגית `v0.4.4`.
3. הורד את artifact בשם `ScreenTimeGuardian-*`.
4. ודא שהחבילה כוללת את התיקיות `Service`, `ControlPanel`, `NativeHost`, `Agent`, `Updater`, `Extension`, `Policies`, `Docs`, ובתוך `NativeHost` גם את `NativeHost.manifest.json`.

## התקנה במחשב Windows

1. הסר גרסה ישנה באמצעות `Policies\Uninstall-Service.ps1` מחלון PowerShell כמנהל.
2. חלץ את החבילה אל `C:\Program Files\ScreenTimeGuardian`.
3. התקן את השירות:

```powershell
cd 'C:\Program Files\ScreenTimeGuardian\Policies'
.\Install-Service.ps1 -ServiceExecutable '..\Service\ScreenTimeGuardian.Service.exe' -StartAfterInstall
```

4. התקן את התוסף ב־Chrome או Edge, וקבל את מזהה התוסף.
5. רשום את ה־Native Host:

```powershell
.\Register-NativeHost.ps1 -ExtensionId <מזהה-התוסף>
```

6. הפעל את מדיניות הדפדפן ואת סוכן ההתראות כמנהל:

```powershell
.\Set-BrowserPolicies.ps1 -ExtensionId <מזהה-התוסף>
.\Install-Agent.ps1 -AgentExecutable '..\Agent\ScreenTimeGuardian.Agent.exe'
```

7. פתח את `ControlPanel\ScreenTimeGuardian.ControlPanel.exe` וקבע את סיסמת הניהול אצל המבוגר האחראי.

## בדיקת קבלה

- התחל עם כלל אחד בלבד על אפליקציה שאינה קריטית.
- השאר `ActivationDelaySeconds` על `0` בבדיקה הראשונה.
- בדוק חלון רגיל, חלון שחוצה חצות וחלון `AllDay`.
- בדוק שוב עם 30 שניות: האפליקציה או האתר זמינים בתחילת החלון, והחסימה מופעלת לאחר ההשהיה.
- בדוק שהשינוי נשאר פעיל כשה־Control Panel סגור.
- אתחל את Windows ובדוק שהמערכת עולה לפני הפעלת נעילת דפדפנים.
- הפעל `BlockUnapprovedBrowserLaunch` רק לאחר שכל שאר הבדיקות עברו.

## חירום

```powershell
.\Emergency-Disable.ps1
```

או צור קובץ ריק בשם `C:\ProgramData\ScreenTimeGuardian\SAFEMODE`. מתג החירום משבית אכיפה; הסרה מלאה מתבצעת באמצעות `Uninstall-Service.ps1`.
