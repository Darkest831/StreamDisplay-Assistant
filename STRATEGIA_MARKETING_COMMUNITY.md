# 📢 STRATEGIA DI MARKETING E DISTRIBUZIONE A COSTO ZERO

La strategia di acquisizione utenti per **StreamDisplay Assistant** si basa sul principio del **"Build in Public & Community Value"**: presentarsi come un appassionato/sviluppatore che ha risolto un problema comune e condivide gratuitamente lo strumento con la community.

---

## 🎯 TARGET E CANALI PRINCIPALI

| Canale | Subreddit / Piattaforma | Tipo di Utente | Problema Chiave |
| :--- | :--- | :--- | :--- |
| **Reddit** | `r/MoonlightStreaming` | Gamer con host PC e client portatili | Risoluzioni 16:10 su Steam Deck e HDR |
| **Reddit** | `r/OLED_Gaming` | Possessori di monitor LG/Samsung/Dell OLED | Paura del burn-in con monitor acceso |
| **Reddit** | `r/cloudgaming` & `r/pcgaming` | Gamer PC avanzati | Automazione streaming locale |
| **Discord** | Sunshine Official, Moonlight Community | Power user e tester | Cercheranno nuove utility veloci |
| **Itch.io** | Sezione "Tools & Utilities" | Utenti che cercano software indie | Download gratuiti e donazioni |

---

## 📝 TEMPLATE POST REDDIT (IN INGLESE - MASSIMA DIFFUSIONE)

I subreddit principali sono in lingua inglese. Ecco i modelli ottimizzati pronti per la pubblicazione:

### Post 1: per `r/MoonlightStreaming`
**Titolo:**
> *I made a lightweight Windows utility to automate 16:10/iPad resolutions, HDR, and OLED blackout when streaming with Moonlight & Sunshine (Free & Open Source)*

**Testo:**
```markdown
Hey everyone!

Every time I streamed my desktop to my Steam Deck (16:10) or iPad (4:3), I had to manually change the resolution in Windows display settings, toggle HDR, and worry about my main OLED gaming monitor staying turned on wasting pixels and power.

I couldn't find a clean, lightweight native tool that did all of this smoothly, so I built **StreamDisplay Assistant** in C# / .NET 8 WPF.

### What it does:
- 🖥️ **Instant Resolution Presets:** 1-click switch to 16:10 (Deck / Legion Go), 4:3 (iPads), or custom refresh rates (60/90/120/180Hz) to kill black bars.
- 🌈 **Windows HDR Toggle:** Fast Win32 toggle without having to dig into Windows Settings.
- 🛡️ **OLED Blackout Shield:** True full-screen pure black (#000000) borderless overlay that physically shuts off OLED pixels on your main display while the stream is running (press ESC or double click to dismiss).
- 📥 **System Tray:** Minimizes cleanly next to the clock.

The entire core utility with all manual controls and OLED blackout is **100% free** on GitHub.

For anyone who wants **zero-touch automation** (it monitors `sunshine.exe` or `moonlight.exe` in the background and automatically switches resolutions, turns on HDR, applies OLED blackout, and restores your native desktop upon exit), I also added a Pro supporter option on Gumroad.

Check out the GitHub release: [https://github.com/tuo-username/StreamDisplay-Assistant]
VirusTotal Scan report: [Link al report VirusTotal]

Let me know what you think and what resolution presets or features you'd like added next!
```

---

### Post 2: per `r/OLED_Gaming`
**Titolo:**
> *Created a tool with an "OLED Blackout" shield and HDR switch for remote play sessions to prevent burn-in*

**Testo:**
```markdown
Hi all!

Like many OLED owners, I get paranoid about burn-in when streaming games to a handheld while my primary OLED screen is sitting with a static desktop or idle game window on the desk.

I put together **StreamDisplay Assistant**, a lightweight Windows 11 tool tailored for OLED owners and game streamers:

- **OLED Blackout Shield:** Instantly drops an uncompressed #000000 pure black fullscreen window over the primary display so OLED subpixels turn completely off while Moonlight/Sunshine streams in the background.
- **Native HDR Switch:** Quick P/Invoke toggle for Windows HDR.
- **Standalone:** Single-file executable, zero bloat, no heavy electron dependencies.

Free download on GitHub: [Link GitHub Release]
Clean VirusTotal scan: [Link VirusTotal]

Hope it helps some fellow OLED owners keep their panel healthy!
```

---

## 💬 GESTIONE RISPOSTE E OBIEZIONI (CONVERSIONE IN ACQUISTI)

### 1. "Perché non usare un semplice script .bat o PowerShell?"
> *"Gli script batch spesso richiedono utilità esterne di terze parti (come QRes o nircmd), faticano a gestire i flag HDR moderni di Windows 10/11 senza riavviare Explorer e non offrono una UI con System Tray, ripristino automatico dello stato precedente e blackout OLED borderless reattivo. Questa è un'app nativa C# a bassissimo consumo che gestisce tutto in un unico posto."*

### 2. "È sicuro? VirusTotal dà falsi positivi?"
> *"Assolutamente sicuro al 100%. Il progetto è compilato direttamente dal codice sorgente con .NET 8 Standalone. Ecco il link al report di scansione VirusTotal con zero rilevamenti: [Link]."*

### 3. "Funziona con i monitor virtuali (Virtual Display Driver / IddSampleDriver)?"
> *"Sì! Puoi selezionare la risoluzione target corrispondente sia che tu stia trasmettendo il monitor fisico primario sia che Sunshine catturi uno schermo virtuale secondario."*

---

## 🔄 IL FEEDBACK LOOP (CONVERSIONE IN VENDITE NELLE PRIME 48 ORE)

1. **Monitora i commenti**: Quando un utente commenta dicendo: *"Funziona alla grande, grazie!"*, rispondi ringraziando e menzionando che gli aggiornamenti futuri includeranno nuove funzionalità richieste.
2. **Se segnalano un bug**: Rilascia una versione patchata entro 24 ore (es. v1.0.1). La rapidità e la disponibilità dello sviluppatore generano fiducia immediata e spingono gli utenti soddisfatti ad acquistare la licenza PRO da 4,99 € su Gumroad come segno di supporto.
