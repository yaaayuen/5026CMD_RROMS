# Gilded redesign (front end only)

The whole site moved from the pink "Berry Bistro" look to the dark-and-gold "Gilded" look.
No C# logic, page handler, form field name, link, route or database code changed.

## What changed

| File | Change |
|---|---|
| `wwwroot/css/site.css` | New colour tokens (light = cream, dark = noir), fonts, buttons, tables, forms, badges, banners, and section 8 "Gilded page layouts" |
| `wwwroot/lib/gilded-fonts/` | Playfair Display, Jost and Pinyon Script served from the site (SIL Open Font License, see the OFL files). No Google Fonts request any more |
| `wwwroot/images/gilded/` | Banner and dish photos (WebP) |
| `Pages/Shared/_Layout.cshtml` | Fonts link, word-mark logo, `data-theme-default` on `<html>`, `data-area` / `data-page` on `<body>` |
| `wwwroot/js/theme.js` | Starting look: saved choice, else the page default (staff/admin light, everyone else dark), else the device |
| `Pages/Index.cshtml` | New landing page layout with descriptive alt text on every food photo |
| `Pages/Account/Login.cshtml` | Split screen: decorative photo panel + the unchanged sign-in form |
| `wwwroot/js/analytics-dashboard.js` | Chart colours only |
| History, Menu, Admin Menu, Promotions edit, `_OrderStyles` | Colour values inside their page `<style>` blocks only |
| `tests/.../Theme/HomePageAltTextTests.cs` | New: every home page image has alt text; the food photos describe the dish |

## How the bold layouts work

Section 8 of `site.css` re-arranges markup the pages already had, chosen by
`<body data-area="Admin" data-page="/Menu/Index">`:

* Role dashboards (Admin, Staff, My dining) become a "bento" grid; each tile is found by its link (`:has(a[href=...])`).
* Admin Accounts and Menu items show their table rows as cards (still a table for screen readers).
* Admin Announcements: the form becomes a dark composer beside a timeline of posts.
* Staff Kitchen orders: a dark kitchen display with four columns of paper tickets.
* Staff table bill: the bill looks like a till receipt next to a dark payment panel.
* Each page's banner photo is picked in section 8.1.

## Accessibility

* Colour pairs still pass WCAG AA in both looks (`ThemeContrastTests`).
* Food photos have alt text that describes the dish; decorative pictures use `alt=""` or CSS backgrounds,
  so screen readers skip them. Screen readers that read what is under the mouse (NVDA, Narrator) read the alt text.
* Animations stop for visitors who ask their device for reduced motion.
