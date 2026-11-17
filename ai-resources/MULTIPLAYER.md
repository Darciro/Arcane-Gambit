# Arcane Gambit — Multiplayer online (decisão e arquitetura)

> Fonte da verdade para o modo online. Atualize este arquivo quando a arquitetura mudar.

## 1. Decisão

| Tema | Escolha | Por quê |
|---|---|---|
| Netcode | **Netcode for GameObjects 2.13.3** | NGO 3.x exige Unity 6000.7; o projeto está em 6000.6.7f1. |
| Sessão, código e conexão | **Multiplayer Services 2.4.1** (Sessions + Relay) | `CreateSessionAsync` já devolve um **código de entrada**, limita a sessão com `MaxPlayers = 2` e conecta pelo Relay sem servidor próprio. |
| Transporte | **Relay por WebSocket (WSS)** em todas as plataformas | A build alvo é **Web**: navegador só fala WebSocket e não aceita conexão direta. Forçar WSS em todas as plataformas deixa o Editor jogar contra o navegador. |
| Topologia | **Client-host** (um jogador hospeda pelo Relay) | 1 contra 1 por turnos: não compensa um servidor dedicado. Distributed Authority foi pensado para muitos jogadores com donos distintos; aqui não acrescenta nada. |
| Sincronização | **Lockstep de comandos, host autoritativo** | `Match` é C# puro e determinístico por seed. Cada jogada já é um comando discreto (`Recruit`, `Move`, `DeclareCapture`, `PlayCard`, `EndTurn`, `React`, `ChooseForesight`). Trafegar só comandos (poucos bytes) é bem mais simples e robusto do que espelhar o estado em `NetworkVariable`s. |
| Mensagens | **Named messages** (`CustomMessagingManager`) | O jogo monta a cena por código, sem prefabs. `NetworkObject`/RPC exigiriam prefabs registrados ou objetos colocados na cena; as mensagens nomeadas dispensam os dois. |

Alternativas descartadas: Netcode for Entities (exagero para um jogo de turnos), espelhar o estado com `NetworkVariable` (mãos, baralhos e marcadores viram muita serialização sem ganho) e Mirror, Fish-Net ou Photon (preferência pelo stack da Unity).

## 2. Arquitetura

```
Rules (C# puro)      Command / Lockstep  ← serializável, testado em LockstepTests
Game                 NetSession          ← NetworkManager + UnityTransport + ISession, persistente (DontDestroyOnLoad)
                     MainMenuMultiplayer ← Salão de Guerra, Entrar com Código e Sala de espera (direção visual v7)
                     GameManager         ← Submit(Command) / PumpNet()
```

- **`Command.Apply(match, side)`** só aplica se `side` for quem a partida espera (`Lockstep.Actor`): Manobras e Presságio → `Current`; Reação → o defensor.
- **`Lockstep.Fingerprint(match)`** é um hash barato do estado visível. O host o envia junto de cada comando, e o convidado confere depois de aplicar. Se divergir, aparece um `Debug.LogError("Online match out of sync…")`.

## 3. Fluxo e protocolo

O desenho aprovado está na direção visual v7, nos quadros "Multiplayer · Fluxo da partida privada", "Salão de Guerra", "Entrar com Código" e "Sala de espera".

1. **Menu › Multiplayer** abre o **Salão de Guerra**, que já tenta fazer o login anônimo. Se os serviços não responderem, aparece a faixa "Sem conexão…" com o botão "Tentar de novo", e as opções Criar e Entrar ficam desabilitadas.
2. **Criar partida** ("Criando sala…") cria uma sessão privada (`MaxPlayers = 2`, Relay, WSS). Nesse momento o anfitrião sorteia a **seed**, que define os exércitos (`NetSession.HostSide`), e abre a **Sala de espera** com o código à vista.
3. **Entrar com Código**: 6 casas. O código ignora maiúsculas, espaços e hífens (`NetSession.CleanCode`). O botão "Entrar na sala" chama `JoinSessionByCodeAsync`. "Cancelar" interrompe a busca sem apagar o código. Os erros aparecem abaixo das casas, que tremem uma vez: código inválido ou sala cheia (um 3º jogador recebe "sala cheia").
4. Ao conectar, o convidado envia `ag.hello`. O anfitrião responde `ag.welcome {seed, tempo de turno}`. O convidado vê "Sala encontrada…" e entra na Sala de espera.
5. Se o anfitrião mudar o tempo de turno (60/90/120 s), ele envia `ag.config {tempo}`.
6. **Iniciar batalha** (só o anfitrião, e só com os dois na sala): envia `ag.start` e **trava a sessão** (`IsLocked`). Os dois fazem a contagem III · II · I e "Às armas!" no próprio relógio, a partir da mesma mensagem, e carregam a cena `Main` juntos (NGO sem scene management).
7. **Saídas da sala:**
   - Esc ou "Sair da sala" pede confirmação ("Encerrar a sala?" para o anfitrião, "Sair da sala?" para o convidado).
   - Se o convidado sai, a sala volta a "Aguardando" e mostra o aviso "O adversário deixou a sala.".
   - Se o anfitrião encerra, o convidado volta ao Salão com o aviso "O anfitrião encerrou a sala.".
8. Durante a partida:
   - O anfitrião aplica a própria jogada e a envia com `ag.command {by, fingerprint, command}`.
   - O convidado só propõe a jogada (`ag.command {command}`). O anfitrião valida e aplica, depois a devolve. Se recusar, envia `ag.reject`.
   - Os comandos remotos esperam o tabuleiro assentar (FX, faixa de turno, avisos), do mesmo jeito que a IA.
9. Se alguém cair durante a partida, aparece o modal "O oponente saiu da partida" e o jogador volta ao menu.

## 4. Configuração necessária (uma vez)

1. **Edit › Project Settings › Services**: vincular o projeto à organização/projeto Unity Cloud (o `cloudProjectId` já existe em ProjectSettings).
2. No **Unity Cloud Dashboard** do projeto: ativar **Lobby** e **Relay**. A autenticação anônima é ativada automaticamente.
3. Testar localmente: **Window › Multiplayer › Multiplayer Play Mode** (instalar `com.unity.multiplayer.playmode` se quiser) ou Editor + build Web. Cada execução entra como um jogador anônimo novo (perfil aleatório), então duas janelas na mesma máquina não colidem.

## 5. Limitações conhecidas / próximos passos

- **Informação oculta:** as duas cópias da partida têm as mãos e os baralhos dos dois jogadores. Um cliente modificado poderia espiar o oponente. Para fechar isso, o host passaria a enviar uma visão filtrada a cada jogador (o convidado deixaria de ter a `Match` completa).
- **Sem revanche online:** no fim da partida, os dois voltam ao menu. Para ter revanche, o host enviaria uma nova `ag.start` com outra seed.
- **Sem reconexão:** se a conexão cair, a partida acaba. As Sessions oferecem `ReconnectToSessionAsync`. A partida poderia ser retomada reaplicando o histórico de comandos guardado pelo host.
- **Copiar e colar na Web:** "Copiar código" usa `navigator.clipboard` (plugin `Plugins/WebGL/Clipboard.jslib`). Colar no campo de código ainda depende do `systemCopyBuffer` da Unity, que no navegador não lê a área de transferência. Para isso seria preciso um plugin de colar (evento `paste`).
- **Tempo de turno:** o valor é escolhido na Sala de espera e sincronizado (`NetSession.TurnSeconds`), mas a partida ainda não o aplica. Falta um desenho de onde o relógio aparece na tela de partida e do que acontece quando o tempo acaba.
- **Nomes dos comandantes:** o desenho mostra nomes ("Comandante Aldric"). Hoje a sala mostra "Comandante" e os avisos falam em "adversário" e "anfitrião", porque ainda não existe um nome de jogador.
- **Aba em segundo plano:** o navegador desacelera abas inativas. Se ficarem muito tempo sem foco, o Relay pode derrubar a conexão.
- **Pausa:** pausar só congela a apresentação local. O oponente continua jogando, e os comandos ficam na fila até a pausa terminar.
