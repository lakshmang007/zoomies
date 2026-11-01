# Launch plan

**Pitch:** Find, organize and clean your files in one light app — with undo for everything.

**Why people switch:** one small app instead of Listary + DropIt + Czkawka + WizTree, and it is
1. **Safe** — a preview before anything moves, the Recycle Bin instead of permanent deletes, and Undo for every action.
2. **Private** — no account, no cloud, no AI service reading your file names.
3. **Light** — a native Windows app, not Electron or an AI wrapper.
4. **Free.**

Each step says who does it:
- 🛠️ **Claude Code**: ask in the Code tab, e.g. "do step 3".
- 🌐 **You + Claude in Chrome**: paste the prompt into the Chrome extension.
- 🙋 **Only you**: anything that needs your sign-in, your ID or your decision.

Claude in Chrome won't type passwords, ID numbers or payment details, and it asks before it submits, posts or accepts anything. Each prompt below says where it will stop for you.

---

## Week 1: get ready

### Step 1 — Choose the new name 🌐 + 🙋
"Folder Pilot" is already used by another Windows Explorer tool, so the app needs a new name before anything goes public.
Replace the candidate list if you have your own ideas.

```text
I'm launching a free Windows desktop app that helps people find, organize and clean up their files
(a File Explorer companion). I need a name that is not already taken. Check each candidate:
Kempt, Tidyfold, Sortful, Pathfinch, Foldwise, Neatnest, Filewisp, Stowly.

For each name:
1. Search the Microsoft Store website (apps.microsoft.com) for apps with the same or a very similar name.
2. Search GitHub for repositories or organizations with that name.
3. Check whether the .com and .app domains look available on a domain registrar's search page. Don't buy anything.
4. Search the web for existing software products or companies using the name.
5. Search the Indian trademark public search (ipindiaonline.gov.in) and the USPTO trademark search for
   live marks in class 9 (software) or class 42 with that name.

Give me one table: name | Store conflict | GitHub conflict | .com / .app | other products | trademark hits | verdict.
Then recommend the best 2 names and explain why. Don't sign up, buy or submit anything.
```
🙋 Pick the name and tell Claude Code: "rename the app to <name>".

### Step 2 — Decide: open source or not 🙋
Recommended: **open source (MIT licence)**. It builds trust for an app that moves and deletes files, it qualifies for free code signing (SignPath Foundation), volunteers can translate it, and it works as a portfolio piece.
Tell Claude Code your choice.

### Step 3 — Make the product launch-ready 🛠️
Ask Claude Code, in this order:
1. "Rename the app to <name>." Covers the code, icon text, window titles, data folder and installer IDs.
2. "Bring idle memory down and measure it." Being light is part of the pitch, so it has to be true; the current figure is ~120 MB with the window open.
3. "Make checkboxes and buttons use one accent colour."
4. "Package it as MSIX for the Microsoft Store." Includes a Store-compatible start-with-Windows setting and a Windows 11 right-click menu.
5. "Set up GitHub Actions to build, test and produce release files."
6. "Write the README, privacy policy and a one-page website."
7. "Record the demo video again with the new name": `tools\DemoRecorder` re-records it in one command.

### Step 4 — Create the GitHub repository 🌐 (sign in first)
```text
I'm signed in to GitHub. Create a new PUBLIC repository named <name> (use PRIVATE if I decided against open
source) with the description "Find, organize and clean your files in one light app — with undo for everything."
Do NOT add a README, licence or .gitignore, because I'll push existing code. After creating it, add these topics:
windows, file-explorer, file-organizer, file-search, productivity, privacy, dotnet, wpf.
Then tell me the HTTPS clone URL. Stop and ask me if anything needs my password or 2FA.
```
🛠️ Then tell Claude Code: "push the code to <clone URL>". Git will open a browser window for you to sign in once.

### Step 5 — Register as a Microsoft Store developer 🌐 + 🙋 (start early; verification can take days)
Registration is free for individual developers.
```text
Open https://storedeveloper.microsoft.com and help me register as an INDIVIDUAL developer on the Microsoft Store.
I will sign in with my own Microsoft account myself, and I will do the identity verification (ID document and
selfie) myself. When you reach any sign-in, ID, selfie or agreement step, stop and tell me what to do.
Where it asks for a publisher display name, propose "<name>" but let me confirm before you continue.
Don't accept any agreement for me: summarise it and wait for my answer.
```

### Step 6 — Reserve the app name in the Store 🌐
```text
I'm signed in to Microsoft Partner Center (partner.microsoft.com/dashboard). Go to "Apps and games",
create a new product of type "MSIX or PWA app", and try to reserve the name "<name>".
If the name is taken, stop and tell me. Don't fill in or submit anything else.
```

---

## Weeks 2–3: beta with real people

### Step 7 — Beta build 🛠️
"Make a beta release on GitHub." Claude Code builds it, writes release notes and publishes it with your approval.

### Step 8 — Feedback form 🌐 (sign in to Google first)
```text
I'm signed in to Google. Create a Google Form titled "<name> beta feedback" with these questions:
1. Which Windows version do you use? (Windows 10 / Windows 11 / Not sure)
2. What did you try? (checkboxes: Quick Jump, Save dialog jump, Organize, Clean up, Workspaces, Undo)
3. What did you like most? (paragraph)
4. What was confusing or broken? (paragraph)
5. How likely are you to keep using it? (linear scale 1–10)
6. Would you recommend it to a friend? (Yes / Maybe / No)
7. Email if you want updates (short answer, optional)
Make all questions optional except 5. Don't share or send the form; give me the link to the form and to its responses.
```

### Step 9 — Recruit 20–50 testers 🙋
Send this message to classmates, friends and family. Use real Windows laptops: Windows 10 and 11, other languages, OneDrive, two monitors.
> I made a free Windows app that finds, organizes and cleans up your files, with undo for everything. Can you try it for a few days and fill in a 2-minute form? Download: <link> · Feedback: <form link>

### Step 10 — Fix what testers report 🛠️
Paste the feedback into Claude Code: "Here's the beta feedback — fix the bugs and the confusing parts."

---

## Week 4: launch

### Step 11 — Submit to the Microsoft Store 🌐 + 🛠️
🛠️ First ask Claude Code: "Prepare the Store listing." You get the description, feature list, keywords, screenshots made from demo data, and the privacy-policy URL.
```text
I'm signed in to Partner Center. Open my app "<name>" and fill in the Store listing from the text below
(description, short description, features, keywords, privacy policy URL, support contact), and upload the
screenshots I'll give you. Set pricing to Free and markets to all. Answer the age-rating questionnaire as:
no violence, no user-generated content, no data collection.
Stop before pressing "Submit for certification" and show me a summary of everything you filled in.

<paste the listing text from Claude Code here>
```

### Step 12 — winget and GitHub release 🛠️
"Publish the 1.0 release and prepare the winget manifest." The winget submission is a pull request that Claude Code prepares and you approve.

### Step 13 — List it on AlternativeTo 🌐 (sign in first)
```text
I'm signed in to alternativeto.net. Help me add a new app called "<name>" (Windows, free, open source if
applicable) with this description: "<pitch + 3 sentences from the README>". Add it as an alternative to:
Listary, DropIt, Czkawka, WizTree, Everything, File Juggler. Website: <url>. Category: File Management.
Show me the filled-in form and wait for my OK before submitting.
```

### Step 14 — Launch-day posts 🛠️ + 🙋
🛠️ "Write the launch posts." You get a separate draft for each of these, each with the demo video or GIF:
- Reddit: r/Windows11, r/windowsapps, r/software, r/productivity, r/3Dprinting.
- Hacker News "Show HN".
- Product Hunt.
- LinkedIn: the student-founder story.

🙋 Post them yourself from your own accounts, one community at a time. Read each subreddit's self-promotion rules first, and reply to every comment on launch day.

### Step 15 — Press and YouTubers 🌐
🛠️ "Write the press email and a press kit."
```text
I'm signed in to Gmail. Create separate DRAFT emails (do not send) to the tips/contact addresses of:
Neowin, Windows Central, XDA Developers, gHacks, How-To Geek. Find each address on the site's own contact
or tips page and tell me where you found it. Use this email text: <press email from Claude Code>.
Attach nothing; I'll add the press-kit link. Leave everything in Drafts for me to review and send.
```
Also find 5–10 smaller YouTubers who make "best free Windows apps" videos and send them the same email from your own account.

---

## After launch: every week
- Answer every review, issue and comment within a day.
- Ship a small update every 2–3 weeks and post what changed.
- Ask for a Store review right after a successful cleanup (Claude Code can add this).
- Invite translators once there are users: Hindi, Spanish, Portuguese, Indonesian, German first.
