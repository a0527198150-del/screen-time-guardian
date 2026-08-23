# מפתח התוסף הקבוע

## מזהה התוסף הקבוע

```
kflejjfoelhfageecelnboegbeoabgge
```

זהו המזהה הנגזר מהמפתח הציבורי. הוא לא ישתנה כל עוד המפתח הפרטי נשמר.

## מה כבר נקבע

- `extension/manifest.json` — מכיל את שדה `"key"` (המפתח הציבורי)
- `src/Guardian.NativeHost/NativeHost.manifest.json` — `allowed_origins` מכיל את המזהה האמיתי

## מה צריך לעשות עכשיו

### 1. שמור את המפתח הפרטי (חובה!)

המפתח הפרטי הודפס בריצת יצירת המפתח. הוא **לא נשמר אוטומטית**.

שמור אותו במקום בטוח (קובץ טקסט, מנהל סיסמאות). **אל תעלה אותו ל־GitHub.**

אם תאבד אותו — המזהה ישתנה, התוסף יישבר, ותצטרך מפתח חדש.

### 2. הוסף אותו כ־GitHub Secret (לצורך אריזת CRX)

Settings ← Secrets and variables ← Actions ← New repository secret

- **Name:** `EXTENSION_PRIVATE_KEY`
- **Value:** הטקסט המלא (כולל שורות `-----BEGIN/END PRIVATE KEY-----`)

### 3. אימות במחשב Windows

```
chrome://extensions ← Developer mode ← Load unpacked ← תיקיית Extension
```

המזהה שמוצג חייב להיות:

```
kflejjfoelhfageecelnboegbeoabgge
```

אם המזהה שונה — המפתח הציבורי לא נקלט נכון, ותקן את שדה `"key"`.

## רישום Native Host

```powershell
.\Register-NativeHost.ps1 -ExtensionId 'kflejjfoelhfageecelnboegbeoabgge' -EdgeExtensionId 'kflejjfoelhfageecelnboegbeoabgge'
```

## גיבוי

- המפתח הפרטי — מחוץ ל־GitHub, במקום בטוח
- `extension/manifest.json` עם `"key"` — בריפו (בטוח, זה ציבורי)
- המזהה — קבוע, אפשר להפיק מחדש מהמפתח הפרטי