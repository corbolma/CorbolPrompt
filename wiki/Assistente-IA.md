# Assistente IA

In CorbolPrompt l'IA si usa in tre modi:

| Dove | Come si usa | A cosa serve |
|---|---|---|
| [Il pannello](#il-pannello) | **Ctrl+Maiusc+A**, poi scrivi in italiano normale | chiedere comandi, spiegazioni, script |
| [I clic nel terminale](#i-clic-nel-terminale) | **✦ Spiega l'errore**, tasto destro | farsi spiegare quello che si vede, senza scrivere niente |
| [La scheda Lisp](#i-comandi-dellia-nel-lisp) | `(ask ...)`, `(ask-data ...)`, `(ai-defun ...)`, `(why)` | usare l'IA dentro un programma, con dati veri |

**Non ci sono comandi speciali da imparare** per il pannello: si scrive una frase. L'IA **non esegue mai niente da sola** nel terminale: propone, e tu premi ▶.

## Cominciare
1. **⋯ → Impostazioni dell'assistente...** e scegli il [servizio](#scegliere-il-servizio). Se usi già Claude Code e hai fatto il login non devi configurare niente.
2. Premi **Ctrl+Maiusc+A** e scrivi, per esempio, *"quali file qui sono più grandi di 100 MB?"*, poi **Invio**.
3. La risposta arriva nel pannello; sotto, ogni comando proposto è un pulsante **▶**. Un clic lo esegue nella scheda attiva.

## Il pannello
### Cosa si può chiedere
| Vuoi... | Scrivi per esempio |
|---|---|
| un comando | *"come trovo i file più grandi di 1 GB?"* · *"quale programma usa la porta 8080?"* |
| capire un errore | *"cosa vuol dire questo errore?"* · *"perché non funziona?"* |
| capire un output | *"riassumi quello che c'è sullo schermo"* · *"quale di questi servizi posso fermare?"* |
| capire un comando | *"cosa fa `robocopy /MIR`? è pericoloso?"* |
| uno script | *"scrivi uno script che rinomina tutte le foto con la data"* |
| usare git | *"crea un repository git qui"* · *"annulla l'ultimo commit senza perdere le modifiche"* |
| tradurre tra shell | *"come si scrive `grep -r` in PowerShell?"* |
| guardare un'altra scheda | *"guarda la scheda server: si è avviato?"* · *"confronta le due schede"* |
| codice Lisp | *"scrivi in Lisp una funzione che conta le parole di un file"* |
| correggere la risposta | *"fallo con cmd"* · *"solo i file .pdf"* · *"spiegamelo più semplice"* |

Le domande successive continuano la conversazione: l'assistente ricorda le ultime 12 battute (6 domande e 6 risposte). Ogni scheda ha la sua; **Nuova chat** la ricomincia.

Per avere risposte migliori: lavora nella scheda e nella cartella giuste (le conosce), **seleziona nel terminale** il testo di cui parli, e di' cosa vuoi ottenere più che come.

### I pulsanti ▶
Sotto la risposta compare un pulsante per ogni comando (al massimo 8). Quelli della risposta precedente vengono sostituiti.

| Azione | Cosa fa |
|---|---|
| **clic** | esegue il comando nella scheda attiva |
| **tasto destro → Incolla senza eseguire** | lo scrive nel terminale: lo modifichi e premi Invio tu |
| **tasto destro → Copia** | lo copia negli appunti |
| **tasto destro → Aggiungi ai preferiti...** | lo salva, con la spiegazione dell'assistente come descrizione |
| **mouse sopra** | mostra il comando intero |

I pulsanti con **[Lisp]** aprono una scheda Lisp, se non c'è già. Se è acceso **Scrivi in tutte le schede affiancate** (⇶), il comando va a tutte.

Prima di premere ▶ **leggi il comando**: l'assistente avverte quando un comando cancella, sovrascrive o cambia impostazioni, ma può sbagliare.

### Tasti e pulsanti della domanda
| Tasto o pulsante | Cosa fa |
|---|---|
| **Ctrl+Maiusc+A** | mostra o nasconde il pannello |
| **Invio** / **Chiedi** | manda la domanda |
| **Maiusc+Invio** | va a capo |
| **Ferma** | interrompe la risposta |
| **↑ / ↓**, **🕘 Recenti** | le domande già fatte (le ultime 200, comuni a tutte le schede) |
| **🎤 Detta** | dettatura vocale di Windows (Win+H) |
| **Nuova chat** | ricomincia la conversazione della scheda |
| **Esc** | torna al terminale |

## I clic nel terminale
| Azione | Cosa chiede all'assistente |
|---|---|
| pulsante **✦ Spiega l'errore** (in basso a destra), o clic sul **segno rosso** nel margine | perché quel comando è fallito, e il comando corretto |
| tasto destro → **Spiega l'errore di questo comando** | lo stesso, per un comando più in alto |
| tasto destro → **Spiega lo schermo con l'assistente** | cosa è successo nelle ultime righe |
| tasto destro con del testo selezionato → **Spiega la selezione con l'assistente** | cosa vuol dire quel testo |
| tasto destro → **Chiedi all'assistente...** | apre il pannello per scrivere la domanda |
| ★ → Gestisci i preferiti → **Descrivi con l'assistente** | scrive la descrizione di un preferito |

Il segno rosso e **✦ Spiega l'errore** ci sono in PowerShell e nel Lisp; nelle altre shell usa *Spiega lo schermo*.

## I comandi dell'IA nel Lisp
*Dalla versione 0.4.0.* Si scrivono in una scheda **Lisp** (freccia **⌄** accanto al +). **Ctrl+C** interrompe l'attesa.

| Comando | Cosa fa |
|---|---|
| `(ask "domanda")` | risponde con un testo |
| `(ask "domanda" dato)` | lo stesso, su un dato: un testo, una lista, le righe di `ps` |
| `(ask-data "richiesta" :fields '(:a :b))` | risponde con una tabella di **dati** che il programma può usare |
| `(ask-data "richiesta" dato :fields '(:a :b))` | ricava la tabella da un testo, una lista o le righe di `ps` |
| `(ai-defun nome (arg) "cosa fa" :examples (...))` | scrive una funzione e la prova sugli esempi |
| `(ai-source 'nome)` | il codice di una funzione scritta da `ai-defun` |
| `(why)` | spiega l'ultimo errore della scheda Lisp e lo corregge |

### Fare una domanda
```lisp
(ask "Come si chiama il comando che mostra l'indirizzo IP?")

; su un file
(ask "Riassumi in tre righe" (read-file "note.txt"))

; sull'output di un comando
(ask "Ci sono errori? Quali?" (run "git status"))

; su dati veri del sistema
(ask "Quale di questi processi è il più strano?"
     (take 20 (ps "Get-Process | Select-Object Name, Id, Path")))
```

### Avere dati, non testo
```lisp
; una tabella dal nulla
(ask-data "Le 3 città più popolose d'Italia" :fields '(:citta :abitanti))

; dati ricavati da un testo libero
(ask-data "Estrai le spese"
          "ieri 12 euro di pranzo, lunedì 45 di benzina"
          :fields '(:cosa :euro))

; e poi si lavora come con qualunque tabella
(sum-by :euro *1)
```
`*1` è l'ultimo risultato. Con `:fields` il risultato è sempre una lista di righe con quei campi.

### Farsi scrivere una funzione
```lisp
(ai-defun giorni-tra (a b) "giorni tra due date \"aaaa-mm-gg\" (b meno a)"
  :examples ((giorni-tra "2026-01-01" "2026-01-31") 30
             (giorni-tra "2024-02-01" "2024-03-01") 29))

(giorni-tra "2026-01-01" "2026-12-25")   ; → 358
```
Negli esempi si alternano una chiamata e il risultato atteso. Se un esempio non torna, l'IA riceve l'errore e riprova, fino a 3 volte. Se la funzione usa comandi di sistema (`sh`, `ps`, file...) **non viene provata**, e viene detto: leggila prima di usarla.

Per ritrovarla ogni volta che apri una scheda Lisp, salvala in `init.lisp`:
```lisp
(append-file (concatenate 'string (getenv "APPDATA") "/CorbolPrompt/init.lisp")
             (ai-source 'giorni-tra))
```

### Capire un errore
```lisp
(car 5)     ; errore
(why)       ; spiega la causa e mostra il codice corretto
```

Tutti i dettagli sono nella [[Guida al Lisp|Lisp#lia-nel-linguaggio]].

## Scegliere il servizio
**⋯ → Impostazioni dell'assistente...**, o un clic sulla riga in cima al pannello, dove è sempre scritto il servizio in uso. Vale per il pannello e per il Lisp.

| Servizio | Cosa serve | Modelli |
|---|---|---|
| **Claude Code** (predefinito) | [Claude Code](https://claude.com/claude-code) installato e il login fatto; usa il tuo abbonamento, niente chiave | quello predefinito dell'account, oppure opus, sonnet, haiku |
| **Claude (API)** | una chiave API di Anthropic (si paga a consumo) | si sceglie dall'elenco |
| **Compatibile OpenAI** | indirizzo, modello e, se il servizio la chiede, chiave: OpenAI, OpenRouter, Ollama, LM Studio... | quello che scrivi |
| **Assistente locale** | un PC con almeno 16 GB di RAM; si scarica una volta (circa 6 GB) | Ministral 3 8B o Qwen3.5 9B |

- **Più account di Claude Code:** si scelgono da **Account**; *Aggiungi...* ne crea uno, *Accedi di nuovo...* rifà il login.
- **Assistente locale:** gratis e i dati non escono dal PC, ma è più lento e meno preciso: va bene per domande semplici. La prima risposta tarda perché il modello deve caricarsi; dopo 5 minuti senza domande si chiude e libera la memoria.

## Cosa viene mandato
| | Quando |
|---|---|
| la domanda, la shell e la cartella della scheda, i nomi (con shell e cartella) delle altre schede, la conversazione della scheda | sempre, con una domanda del pannello |
| le ultime 80 righe dello schermo e il testo selezionato | se **Manda all'assistente le ultime righe del terminale** è acceso (lo è di serie) |
| le ultime 40 righe di un'altra scheda | come sopra, e solo se la nomini nella domanda o parli delle "schede" |
| il comando e il codice di uscita | con **Spiega l'errore** |
| l'output di quel comando | con **Spiega l'errore**, se l'opzione è accesa (fino alla 0.4.0 anche da spenta) |
| solo la domanda e il dato che le passi | con `ask` e `ask-data` |
| il nome, gli argomenti, la descrizione e gli esempi; se un tentativo non va, l'errore | con `ai-defun` |
| il codice e l'errore | con `(why)` |
| la shell e il comando | con **Descrivi con l'assistente** |

I dati vanno al servizio che hai scelto; con l'assistente locale restano sul PC. Se sullo schermo ci sono password o dati riservati spegni l'opzione nelle impostazioni. Le chiavi API sono cifrate con DPAPI di Windows. Con Claude Code l'assistente gira senza strumenti: non legge file e non esegue comandi.

## Se qualcosa non va
| Messaggio o problema | Cosa fare |
|---|---|
| *Claude Code non è installato* | installa [Claude Code](https://claude.com/claude-code) o scegli un altro servizio |
| *login non fatto* | **Impostazioni dell'assistente → Accedi di nuovo...** e scrivi `/login` nella finestra che si apre |
| *chiave API non valida o mancante* | ricontrolla la chiave nelle impostazioni |
| *credito esaurito*, *troppe richieste* | controlla l'account del servizio, o aspetta un po' |
| *modello o indirizzo non trovato* | ricontrolla nome del modello e indirizzo |
| non vede lo schermo o il testo selezionato | accendi **Manda all'assistente le ultime righe del terminale** |
| risponde per la shell sbagliata | passa alla scheda giusta, o scrivilo: *"in cmd"*, *"in bash"* |
| *IA non configurata* (nel Lisp) | scegli il servizio da **⋯ → Impostazioni dell'assistente...** |

Le risposte dell'IA possono essere sbagliate anche quando sembrano sicure: controlla i comandi prima di eseguirli.
