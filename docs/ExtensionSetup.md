# מזהה תוסף קבוע — מדריך מלא

התוסף של שומר זמן מסך צריך מזהה יציב שלא משתנה בכל טעינה מחדש.
בלי מזהה קבוע, ה־Native Host לא מזהה את התוסף, וכל חסימות חשבון Google לא עובדות.

## איך Chrome מחשב מזהה

מזהה תוסף ב־Chrome נגזר מ־SHA-256 של **המפתח הציבורי** (DER) וממופה ל־32 תווים (`a-p`).
אם המפתח לא משתנה — המזהה לא משתנה. לנצח.

---

## שלב 1 — מפתח RSA קבוע (עושים פעם אחת, במחשב Windows)

### שיטה א' — Chrome (הכי פשוט)

```powershell
cd "C:\Program Files\ScreenTimeGuardian"
& "C:\Program Files\Google\Chrome\Application\chrome.exe" `
    --pack-extension=".\Extension"
```

נוצר `Extension.crx` ותיקייה עם `Extension.pem`.

**תשמור את `Extension.pem` במקום בטוח!** אובדן = מזהה חדש = הכל נשבר.
**אל תעלה אותו ל־GitHub.**

### שיטה ב' — PowerShell בלבד (אם אין Chrome)

```powershell
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$pem = $rsa.ExportRSAPrivateKeyPem()
Set-Content -Path "Extension.pem" -Value $pem
```

---

## שלב 2 — חישוב המזהה הציבורי

```powershell
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem((Get-Content "Extension.pem" -Raw))
$pubDer = $rsa.ExportSubjectPublicKeyInfo()
$pubB64 = [Convert]::ToBase64String($pubDer)

$sha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($pubDer)
$id = -join ($sha[0..15] | ForEach-Object {
    [char]([int]'a' + ($_ -shr 4))
    [char]([int]'a' + ($_ -band 0x0f))
})

Write-Host "Extension ID : $id"
Write-Host "Public key   : $pubB64"
```

**תעתיק את שניהם.** המזהה משמש ברישום, המפתח הציבורי נכנס ל־`manifest.json`.

---

## שלב 3 — הטמעה ב־manifest.json

הוסף שדה `"key"` ברמה העליונה של `extension/manifest.json`:

```json
{
  "manifest_version": 3,
  "key": "<המפתח הציבורי מ־pubB64>",
  "name": "שומר זמן מסך",
  ...
}
```

אחרי השינוי, **כל טעינה** (גם Developer Mode) תיתן את אותו מזהה.

---

## שלב 4 — רישום Native Host עם מזהה קבוע

```powershell
.\Register-NativeHost.ps1 `
    -ExtensionId 'pppppppppppppppppppppppppppppppp' `
    -EdgeExtensionId 'pppppppppppppppppppppppppppppppp'
```

אם שמרת את `Extension.pem` בתיקייה, אפשר להריץ:

```powershell
.\Register-NativeHost.ps1 -KeyPath ".\Extension.pem"
```

הסקריפט יחשב את המזהה וירשום אוטומטית עבור Chrome ו־Edge.

---

## שלב 5 — אימות

```powershell
# בדוק שהתוסף רשום
Get-ItemProperty 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host'

# בדוק את המניפסט שנכתב
Get-Content "$env:ProgramData\ScreenTimeGuardian\com.screentimeguardian.host.json"
```

`allowed_origins` צריך להכיל את המזהה שחישבת.

---

## שלב 6 — כפיית התקנה

אחרי פרסום התוסף (Chrome Web Store או GitHub Pages):

```powershell
.\Set-BrowserPolicies.ps1 `
    -ExtensionId 'pppppppppppppppppppppppppppppppp' `
    -EdgeExtensionId 'pppppppppppppppppppppppppppppppp' `
    -UpdateUrl 'https://example.com/chrome/update.xml' `
    -EdgeUpdateUrl 'https://example.com/edge/update.xml'
```

התוסף יותקן אוטומטית, לא יהיה ניתן להסרה, ויפעל לכל משתמשי המחשב.

---

## גיבוי

- `Extension.pem` — גבה במקום בטוח, מחוץ ל־GitHub
- `extension/manifest.json` עם `"key"` — נשמר בריפו
- המזהה — אפשר להפיק מחדש מה־pem

## פתרון בעיות

| בעיה | בדיקה |
|---|---|
| `Specified native messaging host not found.` | `chrome://extensions` → Errors |
| המזהה משתנה אחרי טעינה מחדש | `"key"` חסר ב־`manifest.json` |
| `ExtensionInstallForcelist` באדום ב־`chrome://policy` | כתובת ה־`update_url` לא תקינה |