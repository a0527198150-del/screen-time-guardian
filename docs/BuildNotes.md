# הערות בנייה

## מה נבדק ומה לא

הקוד עבר בדיקת טיפוסים מלאה על לינוקס, מול חיקויים של API של Windows (ראה `tools/`).

| פרויקט | בדיקת טיפוסים | XAML | NuGet |
|---|---|---|---|
| Guardian.Contracts | ✅ 0 שגיאות | — | אין תלויות |
| Guardian.Service | ✅ 0 שגיאות | — | ⚠️ לא נבדק |
| Guardian.ControlPanel | ✅ 0 שגיאות | ✅ נבדק סטטית | אין תלויות |
| Guardian.Agent | ✅ 0 שגיאות | ✅ נבדק סטטית | אין תלויות |
| Guardian.NativeHost | ✅ 0 שגיאות | — | אין תלויות |
| Guardian.LaunchBlocker | ✅ 0 שגיאות | — | אין תלויות |

בדיקת ה־XAML כיסתה: תקינות XML, התאמת `x:Class` למחלקה חלקית בקוד, קיום כל `StaticResource` שבשימוש, תקינות שמות אלמנטים ותכונות, ו־`Grid.Row`/`Grid.Column` בתוך התחום שהוגדר. **היא לא מקמפלת XAML אמיתי** — היא בדיקה סטטית שכתבתי, לא מנוע ה־BAML של WPF.

## הסיכון הפתוח: System.IO.Pipes.AccessControl

`Guardian.Service.csproj` מפנה ל־`System.IO.Pipes.AccessControl` בגרסה **`6.0.0-preview.5.21301.5`**. זו גרסת preview, ואין לה גרסה יציבה מקבילה.

הבעיה: מאז .NET 7, הטיפוסים `PipeSecurity`, `PipeAccessRule` ו־`NamedPipeServerStreamAcl` נכללים במסגרת המשותפת עבור יעדי Windows. ייתכן שההפניה הזו **מיותרת**, וגרוע מכך — ייתכן שהיא יוצרת התנגשות טיפוסים עם אלה שבמסגרת.

**אם ה־build נכשל עם התנגשות טיפוסים סביב `PipeSecurity` או `NamedPipeServerStreamAcl`, נסה לפי הסדר:**

1. **הסר את ההפניה לגמרי** מ־`Guardian.Service.csproj`:
   ```xml
   <!-- <PackageReference Include="System.IO.Pipes.AccessControl" Version="6.0.0-preview.5.21301.5" /> -->
   ```
   זה הפתרון הסביר ביותר עבור `net8.0-windows`.

2. אם שלב 1 נכשל עם "הטיפוס לא נמצא" — החזר, אבל בגרסה היציבה `5.0.0` במקום ה־preview.

3. אם גם זה נכשל — החזר ל־preview והשאר.

## למה NU נמצא ב־WarningsNotAsErrors

`TreatWarningsAsErrors` חל גם על אזהרות NuGet. חבילת preview מייצרת `NU5104`, ובלי החרגה היא לבדה מפילה את ה־CI מסיבה שאין לה קשר לקוד.

## הרצת הבדיקה בעצמך

על לינוקס או WSL, בלי Windows ובלי NuGet:

```bash
apt-get install -y dotnet-sdk-8.0
```

ואז בנה פרויקט זמני שמכיל את קובצי המקור יחד עם `tools/Shims.cs` ו־`tools/WpfShims.cs`, עם `NuGet.config` שבו `<packageSources><clear /></packageSources>`.

זה תופס שגיאות תחביר, טיפוסים חסרים וחתימות שגויות תוך שניות — בלי לחכות ל־CI.
