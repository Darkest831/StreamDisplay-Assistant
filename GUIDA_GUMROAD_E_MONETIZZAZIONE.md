# 💰 GUIDA COMPLETA ALLA MONETIZZAZIONE CON GUMROAD

Questa guida spiega passo per passo come impostare la vendita della licenza PRO di **StreamDisplay Assistant** su **Gumroad** a costo zero, ricevendo i pagamenti direttamente sul tuo conto corrente o PayPal.

---

## 📌 PERCHÉ GUMROAD? (MODELLO A COSTO ZERO)

1. **Zero costi fissi**: Non ci sono canoni mensili, costi di setup o costi di hosting.
2. **Commissione solo sul venduto**: Gumroad trattiene il **10% + piccola commissione di transazione** solo quando effettui una vendita reale.
3. **Generazione automatica delle License Key**: Non devi gestire database server, autenticazione o backend personalizzati. Gumroad genera, invia via email e valida le chiavi tramite le sue API REST ufficiali.
4. **Metodi di pagamento globali**: I clienti possono pagare con Carta di Credito/Debito, Apple Pay, Google Pay e PayPal.
5. **Payout automatici**: I ricavi accumulati vengono versati settimanalmente sul tuo conto bancario italiano (IBAN) o conto PayPal.

---

## 🛠️ PASSO 1: CREAZIONE ACCOUNT E CONFIGURAZIONE PAYOUT

1. Vai su [Gumroad.com](https://gumroad.com) e clicca su **Start selling**.
2. Registrati con la tua email o account GitHub / Google.
3. Vai in **Settings** $\rightarrow$ **Payouts**:
   - Seleziona il tuo paese (**Italy**).
   - Inserisci il tuo nome, cognome, indirizzo e il tuo codice **IBAN** bancario (oppure collega il tuo conto **PayPal**).
   - I bonifici vengono inviati automaticamente ogni venerdì per le vendite maturate.

---

## 📦 PASSO 2: CREAZIONE DEL PRODOTTO "PRO LICENSE"

1. Dalla barra laterale di Gumroad, clicca su **Products** $\rightarrow$ **New product**.
2. Configura le impostazioni iniziali:
   - **Name**: `StreamDisplay Assistant - Pro License`
   - **Type**: Seleziona **Digital product**
   - **Price**: Inserisci `4.99` (Valuta: EUR €)
3. Clicca su **Next: Customize** in alto a destra.

---

## 🎨 PASSO 3: DESCRIZIONE E GRAFICA DEL PRODOTTO

Nella scheda **Product**:
- **Description**: Incolla una descrizione orientata ai benefici:
  ```markdown
  ⚡ Sblocca tutte le funzionalità di automazione zero-touch per StreamDisplay Assistant:
  
  • Rilevamento automatico e istantaneo dell'avvio di Sunshine o Moonlight.
  • Adattamento automatico della risoluzione ideale (Steam Deck 16:10, iPad 4:3, ROG Ally, TV).
  • Attivazione automatica di Windows HDR solo durante la sessione di gioco.
  • Protezione schermo OLED Blackout automatica (#000000 puro) per spegnere i pixel del monitor primario.
  • Ripristino automatico allo spegnimento della sessione (risoluzione nativa e disattivazione HDR).
  • Utilizzabile su un massimo di 3 PC personali dello stesso proprietario.
  • Aggiornamenti gratuiti a vita.
  ```

- **Thumbnail / Cover**: Carica uno screenshot pulito dell'interfaccia con il badge "PRO EDITION" e i pulsanti illuminati.

---

## 🔑 PASSO 4: ABILITAZIONE DELLE CHIAVI DI LICENZA (FONDAMENTALE)

1. Scorri la pagina delle impostazioni del prodotto fino alla sezione **Settings** (o **License Keys**).
2. Spunta la casella: **"Generate license keys"**.
3. **Limit license key uses**: Imposta un limite ragionevole, ad esempio **3 usi** (permette al cliente di attivare il tool sul PC desktop di casa, sul portatile o su un mini-PC, bloccando la pirateria pubblica sui forum).
4. Clicca su **Save and continue** $\rightarrow$ **Publish and continue**.

---

## 🔗 PASSO 5: COLLEGARE IL PRODUCT ID AL CODICE SORGENTE

Gumroad assegna al tuo prodotto un **Permalink** (l'ultima parte dell'URL del prodotto, ad esempio `streamdisplay_pro` o un codice alfanumerico univoco).

### Dove inserirlo:
1. Nel file [`Services/GumroadLicenseService.cs`](file:///f:/Cartella%20progetti/app%20per%20monetizzare/StreamDisplayAssistant/Services/GumroadLicenseService.cs#L55):
   ```csharp
   public const string DEFAULT_PRODUCT_ID = "IL_TUO_PERMALINK_GUMROAD";
   ```
2. Oppure l'utente (o tu in fase di test) può specificare o cambiare l'ID direttamente nella scheda **⚙️ Impostazioni** dell'applicazione alla voce *"ID Prodotto Gumroad"*.

---

## 🧪 PASSO 6: TEST DI VERIFICA (COME FUNZIONA LA CHIAMATA API)

Quando un utente inserisce la chiave e clicca su *"Verifica e Attiva PRO"*, l'applicazione esegue questa chiamata HTTP POST:

```http
POST https://api.gumroad.com/v2/licenses/verify
Content-Type: application/x-www-form-urlencoded

product_id=IL_TUO_PERMALINK&license_key=CHIAVE_DEL_CLIENTE&increment_uses_count=true
```

La risposta di Gumroad è un JSON di questo tipo:
```json
{
  "success": true,
  "uses": 1,
  "purchase": {
    "email": "cliente@email.com",
    "created_at": "2026-10-13T12:00:00Z",
    "refunded": false,
    "chargebacked": false
  }
}
```

- Se `success == true`, l'app cifra i dati localmente con **Windows DPAPI** e sblocca la versione PRO.
- Se `refunded == true` o la chiave è revocata, l'app rifiuta l'attivazione.

> 💡 **Modalità Test Sviluppatore**: Per provare l'attivazione senza acquistare una licenza reale, inserisci la chiave `PRO-DEVELOPER-PASS-2026`.

---

## 📈 STRATEGIA PREZZI E UPSELL

- **Prezzo standard consigliato**: 4,99 € (prezzo d'impulso: sotto la soglia psicologica dei 5 euro, l'utente medio appassionato di streaming acquista senza esitazione).
- **Opzione "Pay what you want"**: Su Gumroad puoi impostare "4,99 € +". Molti appassionati di home-server e Reddit scelgono volontariamente di pagare 7, 10 o 15 € per supportare lo sviluppatore.
