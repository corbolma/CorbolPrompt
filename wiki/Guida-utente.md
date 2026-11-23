# Guida utente

## La finestra
- **In alto:** le schede, il **+** e la freccia **⌄** per aprirne di nuove; a destra **★ Preferiti**, **✦ Assistente** e il menu **⋯**.
- **Al centro:** il terminale della scheda attiva.
- **Sotto il terminale:** i preferiti adatti alla shell della scheda (vedi [Preferiti](#preferiti)).
- **A destra:** il pannello dell'assistente; si mostra e si nasconde con **✦ Assistente** o **Ctrl+Maiusc+A**. Il bordo tra terminale e pannello si trascina.

## Schede
- **+** (o **Ctrl+Maiusc+T**, o doppio clic sullo spazio vuoto della barra): nuova scheda con la shell predefinita, nella cartella della scheda attiva.
- **⌄** accanto al +: nuova scheda scegliendo la shell: PowerShell, Windows PowerShell, Prompt dei comandi, WSL (Linux), Git Bash, Lisp. Compaiono solo le shell installate.
- **⋯ → Shell predefinita**: quale shell apre il +.
- **Ctrl+Tab / Ctrl+Maiusc+Tab**: scheda successiva / precedente. Trascinando una scheda si cambia l'ordine.
- **×**, clic con la rotellina o **Ctrl+Maiusc+W**: chiude la scheda. Scrivere `exit` nella shell chiude la scheda; se il programma termina con un errore la scheda resta aperta: **Invio** lo riavvia.
- **Rinominare:** doppio clic sul nome della scheda (o tasto destro → *Rinomina...*), scrivi il nome e premi **Invio**; **Esc** annulla. Un nome tuo resta fisso, viene ricordato alla riapertura e lo puoi usare con l'assistente (*"guarda la scheda server"*). Svuotalo per tornare al nome automatico.
- **Tasto destro su una scheda:** *Rinomina...*, *Duplica scheda* (stessa shell e cartella), *Riavvia*, *Chiudi*, *Chiudi le altre*, *Chiudi le schede a destra*, *Copia il percorso della cartella*, *Apri la cartella in Esplora file*.
- Se non la rinomini, il nome della scheda è la shell e la cartella corrente (`PowerShell · progetto`), oppure il titolo dato dal programma (in WSL per esempio `utente@pc: ~/cartella`). Passandoci sopra si vede il percorso completo.
- **Molte schede:** quando non ci stanno tutte si scorrono con la rotellina o con il touchpad (anche orizzontale) sopra la barra; il bordo sfuma dove ce ne sono altre nascoste, e il pulsante **≡** a destra mostra l'elenco di tutte.
- La linea verde sotto il nome indica che l'assistente sta rispondendo per quella scheda.
- Chiudendo CorbolPrompt le schede vengono ricordate: alla riapertura tornano, ognuna nella sua cartella (i comandi in corso no).

## Schede affiancate
Il pulsante **▦** nella barra in alto (o **Ctrl+Maiusc+G**, o **⋯ → Affianca tutte le schede**) mostra tutte le schede insieme, in una griglia di riquadri: 2 affiancate, 4 in 2×2, e così via (l'ultima riga può avere riquadri più larghi).
- I **bordi tra i riquadri si trascinano** per ridimensionarli; ogni shell riceve la nuova misura.
- In cima a ogni riquadro c'è una barretta col nome: quella **verde** è la scheda attiva, su cui lavorano assistente e preferiti. Un clic in un riquadro lo rende attivo; doppio clic sulla barretta per rinominare, tasto destro per il menu della scheda.
- Le nuove schede si aggiungono alla griglia, quelle chiuse la lasciano. Ripremendo **▦** si torna a una scheda alla volta; la scelta viene ricordata.

### Scrivere in tutte le schede
Con le schede affiancate compare il pulsante **⇶** (anche **Ctrl+Maiusc+B** o **⋯ → Scrivi in tutte le schede affiancate**): quello che scrivi o incolli nella scheda attiva arriva **a tutte le schede insieme**, così lanci lo stesso comando in più cartelle o shell. Le barrette dei riquadri diventano **arancioni** con il segno ⇶ finché è acceso; ripremi ⇶ per spegnerlo. Anche i comandi dell'assistente e i preferiti eseguiti vanno a tutte. Non viene ricordato alla riapertura: si riparte sempre spento.

## Il terminale
È un terminale vero, come Windows Terminal: le shell girano nella console virtuale di Windows (ConPTY). Funzionano colori (16, 256 e 24 bit), programmi a schermo intero come `vi`, `less`, `htop` o `edit`, caratteri accentati ed emoji.

| Azione | Come |
|---|---|
| Selezionare | trascina col mouse; doppio clic seleziona una parola (un percorso intero); Maiusc+clic estende |
| Copiare | **Ctrl+C** con del testo selezionato, **Ctrl+Maiusc+C**, o tasto destro → *Copia*. Senza selezione Ctrl+C interrompe il comando, come sempre |
| Incollare | **Ctrl+V**, **Ctrl+Maiusc+V** o tasto destro → *Incolla* |
| Scorrere la cronologia | rotellina, barra a destra, **Maiusc+PagSu / PagGiù**; scrivendo si torna in fondo |
| Dimensione del carattere | **Ctrl++ / Ctrl+- / Ctrl+0** o Ctrl+rotellina; anche dal menu ⋯ |
| Cancellare la cronologia | tasto destro → *Cancella la cronologia dello schermo* |
| Cercare nel testo | **Ctrl+Maiusc+F** (vedi sotto) |
| Aprire un indirizzo o un file | **Ctrl+clic** (vedi sotto) |

Nei programmi a schermo intero (less, man, vim) la rotellina manda le frecce su e giù.

### Cercare nel testo
Il pulsante **🔍 Cerca** nella barra in alto, **Ctrl+Maiusc+F** (o tasto destro → *Cerca nel testo...*) apre una barra sopra il terminale; se c'è del testo selezionato diventa la ricerca. Mentre scrivi tutte le occorrenze nello schermo e nella cronologia si colorano, quella corrente in arancione, e accanto c'è il conteggio (*3 di 12*).
- **Invio** o **↑** va al risultato precedente (più in alto), **Maiusc+Invio** o **↓** al successivo; **F3 / Maiusc+F3** fanno lo stesso anche dal terminale.
- **Aa** distingue maiuscole e minuscole.
- **Esc** o **✕** chiude la barra. La ricerca segue la scheda attiva e si aggiorna con l'output nuovo.

### Link e percorsi cliccabili
Passando col mouse su un **indirizzo web** (`https://...`, `www....`) o su un **percorso** che esiste (`C:\cartella\file.txt`, un percorso tra virgolette anche con spazi, o il nome di un file o di una cartella della cartella corrente, per esempio nell'elenco di `dir`) questo si sottolinea; **Ctrl+clic** lo apre:
- gli indirizzi nel browser;
- le cartelle in Esplora file;
- i file con il loro programma. Programmi e script (`.exe`, `.bat`, `.ps1`...) **non vengono avviati**: si apre Esplora file con il file selezionato.

`file.cs:12` (il formato degli errori dei compilatori) apre il file. Tasto destro su un link: *Apri*, *Copia il link / il percorso*, *Mostra in Esplora file*.

### Comandi falliti
In PowerShell, nel Prompt dei comandi e nel Lisp il terminale sa dove inizia e finisce ogni comando. Quando un comando **fallisce** (in PowerShell e nel Lisp), nel margine sinistro compare un **segno rosso** lungo le sue righe e in basso a destra il pulsante **✦ Spiega l'errore**:
- il pulsante (o un clic sul segno rosso, o tasto destro → *Spiega l'errore di questo comando*) manda all'assistente il comando, il codice di uscita e il suo output, e lui spiega cosa è andato storto e propone il comando corretto come pulsante ▶;
- il pulsante sparisce al comando successivo; i segni rossi restano finché le righe sono nella cronologia.

Il Prompt dei comandi non fa sapere il codice di uscita, quindi lì i segni rossi non ci sono; in WSL e Git Bash nemmeno.

### Spostarsi tra i comandi
**Ctrl+Maiusc+↑** porta in cima allo schermo il comando precedente, **Ctrl+Maiusc+↓** il successivo (un segno azzurro nel margine indica dove sei); utile nei lunghi output. Tasto destro sulle righe di un comando: **Copia il comando** e **Copia l'output del comando** (solo il suo output, senza prompt).

### Avviso a fine comando
Quando un comando che dura almeno **10 secondi** finisce in una scheda che non stai guardando, sul nome della scheda compare un **pallino**: verde se è andato bene, rosso se è fallito; sparisce aprendo la scheda. Se CorbolPrompt è in secondo piano o ridotto a icona, la sua icona lampeggia nella barra delle applicazioni e arriva una **notifica di Windows** con il comando e la durata: un clic riporta alla scheda. Si spegne da **⋯ → Avvisa quando finisce un comando lungo**. Funziona in PowerShell, nel Prompt dei comandi e nel Lisp.

### Trascinare i file
Trascinando file o cartelle da Esplora file sul terminale se ne scrive il **percorso**, tra virgolette se serve e nel formato della shell (`/mnt/c/...` in WSL, `/c/...` in Git Bash, stringa nel Lisp). Il comando non parte: completi e premi Invio.

### Il menu del tasto destro sul terminale
*Apri*, *Copia il link*, *Mostra in Esplora file* (su un link), *Spiega l'errore di questo comando* (su un comando fallito), *Copia*, *Incolla*, *Seleziona tutto*, *Aggiungi ai preferiti...* (con del testo selezionato), *Cerca nel testo...*, *Spiega la selezione / lo schermo con l'assistente*, *Chiedi all'assistente...*, *Cancella la cronologia dello schermo*.

## L'assistente
Il pannello a destra. In cima c'è sempre scritto il **servizio** in uso e, per Claude Code, **l'account** (email): clic per cambiarli.

- Scrivi una domanda e premi **Invio** (Maiusc+Invio va a capo, Esc torna al terminale). Esempi: *"come trovo i file più grandi di 1 GB?"*, *"cosa vuol dire questo errore?"*, *"crea un repository git qui"*.
- L'assistente riceve la tua domanda, la shell e la cartella della scheda, i nomi delle altre schede e, se lo permetti, le **ultime righe dello schermo** e il testo selezionato. Se nella domanda nomini un'altra scheda (o parli delle "schede"), riceve anche le sue ultime righe.
- Ogni comando che propone diventa un pulsante **▶** sotto la conversazione: **clic** lo esegue nella scheda attiva, **tasto destro** lo incolla senza eseguirlo, lo copia o lo aggiunge ai preferiti. Il codice Lisp va in una scheda Lisp (la apre se non c'è). Niente viene eseguito senza il tuo clic.
- I pulsanti sotto la domanda: **🎤 Detta** (dettatura vocale di Windows, Win+H: parli e il testo compare nella domanda), **🕘 Recenti**, **Nuova chat**, **Chiedi** (diventa **Ferma** mentre l'assistente lavora).
- **Domande già fatte:** **↑ / ↓** nel campo della domanda le ripropongono, come nel terminale; **🕘 Recenti** mostra le ultime in un menu. Sono comuni a tutte le schede e restano tra un avvio e l'altro (le ultime 200); il menu ha anche *Cancella la cronologia delle domande*.
- Ogni scheda ha la sua conversazione; **Nuova chat** la ricomincia.
- **Spiega lo schermo con l'assistente** (tasto destro sul terminale) chiede la spiegazione delle ultime righe, o del testo selezionato.

### Impostazioni dell'assistente
**⋯ → Impostazioni dell'assistente...**, oppure clic sulla riga del servizio:

| Servizio | Cosa serve |
|---|---|
| **Claude Code** (predefinito) | [Claude Code](https://claude.com/claude-code) installato e il login fatto: usa il tuo abbonamento, niente chiave. Con più abbonamenti scegli l'**Account**; *Aggiungi...* crea un account e apre Claude Code per il login, *Accedi di nuovo...* rifà il login |
| **Claude (API)** | una chiave API di Anthropic |
| **Compatibile OpenAI** | indirizzo, chiave e modello (OpenAI, OpenRouter, Ollama, LM Studio...) |
| **Assistente locale** | si scarica una volta (modello e llama.cpp); gira sul PC, i dati non escono. Serve un PC con almeno 16 GB di RAM |

**Manda all'assistente le ultime righe del terminale**: acceso di serie. Spegnilo se sullo schermo ci sono password o dati riservati; l'assistente vedrà solo la domanda, la shell e la cartella. Le chiavi API sono cifrate con DPAPI di Windows.

Gli account di Claude Code sono condivisi con le altre app Corbol.

## Preferiti
I comandi da riusare, ognuno con **nome**, **descrizione**, **shell** e **comando** (anche su più righe).

- **Aggiungere:** seleziona il comando nel terminale → tasto destro → **Aggiungi ai preferiti...**; oppure tasto destro su un pulsante ▶ dell'assistente → **Aggiungi ai preferiti...**: in questo caso la descrizione è già la spiegazione dell'assistente. Si apre la finestra Preferiti con il nuovo comando: controlla nome e shell e premi OK.
- **Usare:** la **barra sotto il terminale** mostra i preferiti della shell della scheda attiva (e quelli per *qualsiasi shell*): **clic** esegue, **Maiusc+clic** incolla senza eseguire, passandoci sopra si vedono descrizione e comando. Lo stesso dal menu **★ Preferiti** (**Ctrl+Maiusc+P**), dove quelli delle altre shell sono in *Altre shell*.
- **Gestire:** ★ → **Gestisci i preferiti...**: elenco a sinistra, a destra nome, shell, descrizione e comando; **Nuovo**, **Elimina**, **↑ ↓** per l'ordine. **Descrivi con l'assistente** fa scrivere a lui una descrizione di una o due frasi (anche avvisi come "cancella i file").
- La barra si spegne da **⋯ → Suggerisci i preferiti sotto il terminale**.

## La scheda Lisp
Un piccolo Common Lisp interpretato, con comandi per il sistema: vedi la [[Guida al Lisp|Lisp]].

## Il menu ⋯
| Voce | Cosa fa |
|---|---|
| Aggiorna a CorbolPrompt X.Y.Z... | compare quando c'è una versione nuova |
| Nuova scheda, Shell predefinita | come la freccia ⌄; quale shell apre il + |
| Impostazioni dell'assistente... | servizio, account, assistente locale |
| Carattere più grande / più piccolo / normale | dimensione del testo del terminale |
| Cerca nel testo... | la barra di ricerca |
| Affianca tutte le schede, Scrivi in tutte le schede affiancate | vedi [Schede affiancate](#schede-affiancate) |
| Lingua | cambia subito, senza chiudere le schede |
| Suggerisci i preferiti sotto il terminale | mostra o nasconde la barra dei preferiti |
| Avvisa quando finisce un comando lungo | pallino sulla scheda e notifica di Windows |
| Benvenuto..., Guida, Guida al Lisp | la presentazione iniziale e queste pagine |
| Scorciatoie da tastiera | l'elenco dei tasti |
| Controlla aggiornamenti, Aggiorna automaticamente | vedi [[Installazione]] |
| Informazioni su CorbolPrompt | versione, licenza, cartella delle impostazioni |

## Tasti
| Tasto | Azione |
|---|---|
| Ctrl+Maiusc+T / Ctrl+Maiusc+W | nuova scheda / chiude la scheda |
| Ctrl+Tab / Ctrl+Maiusc+Tab | scheda successiva / precedente |
| Ctrl+C (con selezione), Ctrl+Maiusc+C | copia |
| Ctrl+V, Ctrl+Maiusc+V | incolla |
| Ctrl+Maiusc+A | assistente |
| Ctrl+Maiusc+P | preferiti |
| Ctrl+Maiusc+G | schede affiancate / una alla volta |
| Ctrl+Maiusc+B | scrivi in tutte le schede affiancate |
| Ctrl+Maiusc+F | cerca nel testo |
| F3 / Maiusc+F3 | risultato precedente / successivo |
| Ctrl+Maiusc+↑ / ↓ | comando precedente / successivo |
| Ctrl+clic | apre l'indirizzo o il percorso sotto il mouse |
| Ctrl+F1 | questa guida |
| Maiusc+PagSu / PagGiù | scorre la cronologia dello schermo |
| Ctrl++ / Ctrl+- / Ctrl+0 | dimensione del carattere |

Tutti gli altri tasti vanno alla shell: frecce, Tab, Alt+lettera (scorciatoie di bash e PSReadLine), F1-F12 ecc.

## Dove sono i file
In `%APPDATA%\CorbolPrompt`:

| File | Contenuto |
|---|---|
| `settings.txt` | impostazioni (anche quelle dell'assistente; le chiavi sono cifrate) |
| `session.txt` | le schede da riaprire |
| `favorites.json` | i preferiti |
| `lisp-history.txt` | la cronologia della scheda Lisp |
| `init.lisp` | se lo crei, viene caricato all'apertura di ogni scheda Lisp |

L'assistente locale scarica i suoi file in `%LOCALAPPDATA%\CorbolPrompt\ai` (o usa quelli già scaricati da un'altra app Corbol).
