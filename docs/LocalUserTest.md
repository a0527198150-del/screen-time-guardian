# בדיקת `-LocalUser` בחומת האש

**אזהרה: בדיקה זו מעולם לא התבצעה על המחשב הספציפי. חובה להריץ אותה
לפני שסומכים על מודל "החסימה חלה עליי ולא על האחים".**

## הבעיה

השירות יוצר חוקי חומת אש עם `-LocalUser` ו־SDDL שמגביל לחשבון Windows ספציפי.
אם `-LocalUser` לא עובד כצפוי, יש שתי תוצאות אפשריות — שתיהן גרועות:

- החוק חל על **כולם** (האחים ייחסמו בטעות)
- החוק חל על **אף אחד** (המשתמש יחשוב שהוא חסום ולא יהיה)

## בדיקה ידנית

```powershell
# 1. קבל את ה־SID של משתמש הבדיקה
$sid = (New-Object System.Security.Principal.NTAccount("שם-המשתמש")).Translate(
        [System.Security.Principal.SecurityIdentifier]).Value
Write-Host "SID: $sid"

# 2. צור חוק ידני על אפליקציה לא חשובה (למשל Notepad++)
New-NetFirewallRule -Name 'STG-TEST' -DisplayName 'STG Test' `
    -Direction Outbound -Action Block -Enabled True -Profile Any `
    -Program 'C:\Windows\System32\notepad.exe' `
    -LocalUser "D:(A;;CC;;;$sid)"

# 3. אמת שהחוק נוצר עם הסינון
Get-NetFirewallRule -Name 'STG-TEST' | Get-NetFirewallSecurityFilter
```

## מה לבדוק

| בדיקה | ציפייה |
|---|---|
| התחבר כמשתמש הבדיקה, פתח את Notepad, נסה לגלוש | **אין אינטרנט** |
| התחבר **כמשתמש אחר**, פתח את Notepad, נסה לגלוש | **יש אינטרנט** |

**שתי הבדיקות חייבות לעבור.** אם רק הראשונה עוברת — הסינון לא עובד והחוק גלובלי.

## ניקוי

```powershell
Remove-NetFirewallRule -Name 'STG-TEST'
```

## אם זה לא עובד

1. **פורמט SDDL חלופי** — נסה `"D:(A;;CC;;;$sid)"` לעומת `"(A;;CC;;;$sid)"`
2. **בדיקת גרסת Windows** — `-LocalUser` בחוק יוצא מתנהג שונה בין Windows 10 Home ל־Pro
3. **דווח** — אל תשאיר קוד שמתיימר לסנן לפי משתמש ולא מסנן. עדיף להשבית את הפיצ'ר ולתעד שהחסימה חלה על כל המחשב, מאשר להשאיר הבטחה שקרית.