# App Corbol

Le app Corbol installate sullo stesso PC lavorano insieme: un'app ne apre un'altra con un file o una cartella, le manda un comando anche quando è già aperta e riceve la risposta. Anche l'assistente IA può usarle, sempre dopo la tua conferma.

Il collegamento c'è da queste versioni: **CorbolImg 1.9.0**, **CorbolIp 1.13.0**, **CorbolPad 1.4.0**, **CorbolPrompt 0.5.0**, **CorbolSys 1.7.0**. Un'app più vecchia si apre lo stesso, ma non riceve i comandi: aggiornala.

## Nei menu
| App | Dove | Cosa fa |
|---|---|---|
| CorbolPad | *File → Apri in* | apre il file della scheda in un'altra app Corbol |
| CorbolPrompt | menu ⋯ → *Apri la cartella in* | apre la cartella della scheda in un'altra app (CorbolImg, ...) |
| CorbolImg | tasto destro sull'immagine → *Apri in* | apre l'immagine in un'altra app |
| CorbolSys, CorbolIp | menu → *App Corbol* | avvia le altre app Corbol o le porta davanti |

Nell'elenco compaiono solo le app installate che sanno aprire quel file. Se l'app è già aperta, il file si apre lì (in una scheda nuova); se è chiusa, si avvia.

## L'assistente IA
In CorbolPad, CorbolPrompt e nell'analisi di CorbolSys l'assistente sa quali altre app Corbol sono installate e può usarle quando glielo chiedi, per esempio:
- *«apri questo testo in una scheda di CorbolPad»*
- *«prepara in CorbolPrompt il comando per vedere le cartelle più grandi»*: il comando viene scritto in una scheda nuova ma **non parte**, lo esegui tu con Invio
- *«chiedi a CorbolSys come sta il PC»*, *«com'è la rete?»* (CorbolIp)

Prima di fare qualunque cosa l'app mostra cosa vuole fare l'assistente e chiede **Procedo?**. Il risultato compare nella conversazione e l'assistente lo vede alla domanda successiva.

## Comandi
| App | Comandi |
|---|---|
| CorbolPad | `open` file · `text` titolo, testo: scheda nuova con quel testo |
| CorbolPrompt | `open` cartella: scheda nuova lì · `paste` comando, [cartella]: comando scritto ma non eseguito |
| CorbolImg | `open` immagini o cartella · `convert` file o cartelle: convertitore |
| CorbolSys | `status`: stato del PC in breve · `report`: analisi completa (circa 10 secondi) |
| CorbolIp | `status`: stato della rete (lo stesso testo di *Copia tutto*) |

Tutte rispondono anche a `show` (porta la finestra davanti), `ping` (nome e versione) e `verbs` (i comandi che conosce).

## Sicurezza
- Il collegamento è una *named pipe* di Windows, `CorbolLink.<App>.<utente>.<sessione>`, aperta **solo al tuo utente** e chiusa alle connessioni dalla rete.
- Nessun comando cancella o modifica file; `paste` non esegue il comando.
- Le azioni chieste dall'assistente partono solo dopo la tua conferma.

## Per sviluppatori
Il codice è in `CorbolLink.cs`, identico in tutte le app. Richiesta (UTF-8, una riga per campo, `\`, a capo e ritorno scritti come `\\`, `\n`, `\r`): `CORBOL/1`, il comando, il numero di argomenti, un argomento per riga. Risposta: `ok` oppure `err`, poi il testo.
