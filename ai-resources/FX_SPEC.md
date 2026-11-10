# Arcane Gambit — Especificação de FX (Feel + Epic Toon FX)

> **Para o Claude Code.** Este documento é a fonte da verdade para implementar os efeitos visuais e o feedback de jogo. Leia-o por inteiro antes de começar. Trabalhe **fase a fase** e **pare ao fim de cada fase** com um relatório (o que mudou, arquivos, o que o Rick precisa fazer no Inspector, como testar). Não avance sem aprovação.

---

## 0. Contexto

- **Jogo:** Arcane Gambit — híbrido de cartas e xadrez, tabuleiro 8×8, dois exércitos (Branco e Negro), cartas de Reação, Equipamento, Tática, Suprimento, Fortificação, Arcanismo e Condição climática.
- **Direção de arte:** manuscrito medieval — tinta sobre pergaminho, poucas cores, traço à mão. **Os efeitos precisam parecer tinta ganhando vida, nunca neon.**
- **Assets já comprados:**
  - **Feel** (More Mountains) — feedback de jogo, orquestrado pelo `MMF_Player`.
  - **Epic Toon FX** (Archanor VFX, v1.82) — mais de 390 efeitos de partículas, estilo cartoon saturado. **Precisa ser recolorido** para a paleta do jogo.
- **Divisão de papéis:**
  - **Feel = como o jogo reage:** tempo (freeze), tremor, molas e escala, interface, textos flutuantes, pós-processo do URP, som e vibração.
  - **Epic Toon FX = o que aparece:** projéteis, explosões, impactos, magia, chuva, fumaça, respingos, brilhos. Entra no `MMF_Player` como feedback **Particles Instantiation** (com pool).
- **Regra de ouro:** um `MMF_Player` por evento de jogo. A lógica das regras **nunca** chama efeitos diretamente — ela emite eventos; a camada de apresentação decide o que tocar.

---

## 1. Regras de trabalho para o Claude Code

1. **Verifique antes de escrever código.** Leia `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, o URP Asset ativo, a pasta do Feel e a do Epic Toon FX. **Nenhuma API do Feel é presumida:** confira nomes de classes, propriedades e métodos no código-fonte do Feel instalado no projeto. Os nomes de feedback neste documento são os do menu *Add new feedback* do Inspector; a classe C# correspondente pode ter outro nome (ex.: `MMF_Scale`).
2. **Não atualize pacotes** (Unity, URP, Feel, ETFX) sem me perguntar.
   - Feel 6.3 (out/2026) exige Unity **6000.5.0f1+**. Em Unity 6 mais antigo, a versão compatível do Feel é outra (a 5.9.1 aceita 6000.0.23f1+). Apenas reporte a situação.
3. **Nunca edite os arquivos originais do Feel nem do Epic Toon FX.** Para o ETFX, crie **Prefab Variants com sufixo `_AG`** e materiais próprios.
4. **C# 9** (sem file-scoped namespace, global using, primary constructors etc.). Siga as convenções que já existem no projeto; se não houver, use `[SerializeField] private` com `_camelCase`, um tipo por arquivo e namespace por módulo.
5. **Assíncrono com `Awaitable`** e `destroyCancellationToken`. `async void` só como ponto de entrada de evento, sempre com `try/catch`.
6. **Estado estático:** se existir, resetar em `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` (o Play Mode pode estar sem domain reload).
7. **Código de Editor** em assembly Editor-only (ou `#if UNITY_EDITOR`).
8. **Não invente comprovação.** Não diga que compila ou que o teste passou sem ter rodado. Liste o que ficou pendente de teste no Editor.
9. **Configuração de Inspector** que você não conseguir fazer por código: entregue como checklist passo a passo para o Rick.
10. **Commits pequenos**, um por fase ou subfase, com mensagem clara.

---

## 2. Fase 0 — Reconhecimento (somente leitura)

Não altere nada. Entregue um relatório curto com:

1. Versão exata do Unity, pipeline (URP? versão), plataforma alvo.
2. Feel: versão instalada, onde está, se existe `MMF_Player` em uso. Se a versão é compatível com o Unity do projeto.
3. Epic Toon FX: onde está, se o **upgrade de URP** (pasta `Upgrades`) já foi importado (materiais rosa = não). Liste as subpastas de prefabs e as cenas de demonstração (`Demo/Scenes`).
4. Como o jogo é renderizado:
   - Tabuleiro, peças e cartas são **UGUI, UI Toolkit, sprites ou 3D**?
   - Qual o **Render Mode** dos Canvas? Câmera **ortográfica** ou perspectiva?
5. Onde nascem os eventos de jogo: carta jogada, peça movida, captura, captura bloqueada por reação, equipamento, Moral, condição climática adicionada ou removida, início e fim de rodada e de turno, fim de partida. Existe uma camada de apresentação ou fila de animações? Como o input é bloqueado hoje?
6. Cursores do jogo (existem 6: padrão, clicável, ataque, indisponível, aguardar, arrastar): onde são aplicados.
7. Configuração de Enter Play Mode (domain reload ligado ou desligado) e asmdefs existentes.
8. **Plano proposto** para as fases seguintes, adaptado ao que encontrou, com riscos (por exemplo, mudar Canvas de Overlay para Camera).

**Pare aqui e espere aprovação.**

---

## 3. Fase 1 — Infraestrutura de cena e projeto

### 3.1 Pastas

Use a convenção do projeto. Se não houver, use:

```
Assets/_Project/FX/
  Runtime/        (scripts de runtime + asmdef)
  Editor/         (ferramentas de Editor + asmdef Editor-only)
  Tests/EditMode/
  Players/        (prefabs com MMF_Player por evento)
  ETFX_AG/        (variantes recoloridas dos prefabs do ETFX)
  Materials/
  Palettes/       (ScriptableObjects de rampas de cor)
  Sprites/        (círculo de runas, mancha de tinta, insígnias)
  Scenes/FX_Sandbox.unity
```

### 3.2 Epic Toon FX no URP

- Se os materiais estiverem rosa, avise o Rick para importar o pacote da pasta `Upgrades` do ETFX. Não converta à mão.
- **Câmera ortográfica:** Soft Particles não funcionam. Nos materiais `_AG`, desligue Soft Particles **ou** ative *Depth Texture* no URP Asset. Informe qual foi escolhido.

### 3.3 Partículas por cima da interface

- Partículas **não aparecem** num Canvas *Screen Space – Overlay*.
- Se o tabuleiro ou as cartas forem UGUI em Overlay, **proponha** (não faça sem aprovação) migrar para *Screen Space – Camera*.
- Crie as Sorting Layers, nesta ordem:

  `Tabuleiro` → `FX_Tabuleiro` → `HUD` → `FX_UI` → `Modais`

- Os ParticleSystemRenderer dos prefabs `_AG` usam `FX_Tabuleiro` (efeitos no tabuleiro) ou `FX_UI` (efeitos em cartas e painéis).

### 3.4 Objetos de cena do Feel

Crie um prefab `FX_Root` para a cena de partida com o que o Feel precisa. Confira os nomes exatos no Feel instalado.

| Componente | Para quê |
|---|---|
| MMTimeManager | Freeze Frame |
| MMFlash numa Image de tela cheia (Sorting `FX_UI`, raycast desligado) | Flash |
| MMSoundManager | Sound (o áudio vem numa fase futura; deixe os slots prontos) |
| MMFloatingTextSpawner | Floating Text |
| Volume URP global com Vignette, Color Adjustments e Bloom + os shakers correspondentes do Feel (ex.: `MMBloomShaker_URP`) | Feedbacks de pós-processo |
| Container `Players/` | Os MMF_Players globais (Seção 5) |

### 3.5 Regras de alvo do Feel

- O Feel espera **escala 1,1,1** no alvo de feedbacks de escala. Cartas e tokens ganham um filho **`Holder`**; os feedbacks animam o `Holder`, nunca a raiz.
- **O tremor vai no tabuleiro, não na câmera.** Use Position Shake no container do tabuleiro; a HUD fica parada. Com Canvas em Screen Space – Camera, a interface acompanha a câmera, então Camera Shake nem funcionaria para o tabuleiro em UI.
- **Freeze Frame mexe no `Time.timeScale`.** O que precisa continuar durante o freeze (interface, partículas de UI) roda em tempo *unscaled*.

**Entrega da Fase 1:** `FX_Root` montado, Sorting Layers, checklist de Inspector e cena `FX_Sandbox` vazia. Pare.

---

## 4. Fase 2 — Ferramenta de recolor do Epic Toon FX

O ETFX é cartoon saturado. Recolorir à mão 30+ prefabs é lento e inconsistente, então crie uma ferramenta de Editor.

### 4.1 Paleta (únicas cores permitidas nos efeitos)

| Nome | Hex | Uso |
|---|---|---|
| Tinta | `#3B2A1A` | contornos, fumaça densa, chuva |
| Tinta escura | `#2B1D10` / `#1E140A` | manchas, contorno do raio |
| Tinta diluída | `#5A4128` | fumaça, poeira, névoa |
| Rubrica | `#8E2F21` | magia, fogo profundo |
| Rubrica clara | `#C0573F` | fogo, halo do raio, brasas |
| Pergaminho | `#ECDFC4` | neve, névoa clara |
| Luz de vela | `#F3E8D2` | núcleo do fogo e do raio, flash |

### 4.2 Rampas (Color over Lifetime)

Crie um ScriptableObject `FxPalette` com um `Gradient` e um modo de blend.

| Rampa | Chaves de cor (posição → cor, alfa) | Blend |
|---|---|---|
| **Fogo / explosão** | 0% `#F3E8D2` α1 · 30% `#C0573F` · 58% `#8E2F21` · 82% `#3B2A1A` α0,8 · 100% `#3B2A1A` α0 | Aditivo só no núcleo; o resto, Alpha Blended |
| **Raio** | núcleo `#F3E8D2`, contorno `#1E140A`, halo `#C0573F` α0,45 | Aditivo no núcleo |
| **Fumaça / nuvem / névoa** | 0% `#5A4128` α0,6 → 100% `#3B2A1A` α0 | Alpha Blended |
| **Chuva** | `#3B2A1A` α0,35 constante, traço fino | Alpha Blended |
| **Neve** | `#ECDFC4` α0,9 → α0 (contorno de tinta se o sprite permitir) | Alpha Blended |
| **Arcano / runas** | 0% `#8E2F21` → 55% `#C0573F` → 100% α0 | Alpha Blended |
| **Mancha de captura** | `#2B1D10` α0,85 constante (fica como mancha) | Alpha Blended |

### 4.3 Ferramenta `EtfxVariantTool` (Editor)

Menu: **Arcane Gambit ▸ FX ▸ Criar variante _AG**. Recebe o prefab selecionado do ETFX + um `FxPalette`.

1. Instancia o prefab e salva como **Prefab Variant** `<nome>_AG` em `FX/ETFX_AG/`.
2. Em cada `ParticleSystem` filho:
   - `main.startColor = branco`;
   - `colorOverLifetime` ligado com o gradiente da paleta;
   - desliga ou neutraliza Color by Speed e Start Color em gradiente; registra no log o que alterou.
3. Troca os materiais por versões `_AG`: shader de partículas do URP, blend conforme a paleta, Soft Particles conforme a Fase 1.
4. Aplica um **multiplicador de tamanho** opcional:
   - impacto deve caber numa casa do tabuleiro;
   - clima cobre o tabuleiro, nunca a HUD.
5. Define Sorting Layer e ordem.
6. Produz um relatório no Console por variante criada.

### 4.4 Escolha dos prefabs (o Rick escolhe)

Você não consegue ver os efeitos; o Rick escolhe visualmente nas cenas de demonstração.

1. Para cada tipo da coluna **Epic Toon FX** da Seção 8, liste **2–4 candidatos** pelo caminho no projeto.
2. Gere `Docs/FX/FX_ESCOLHAS.md` com a tabela: Evento | Tipo procurado | Candidatos | **Escolhido** (em branco) | Paleta.
3. Depois que o Rick preencher, gere as variantes `_AG` com a ferramenta.

**Entrega da Fase 2:** ferramenta, `FxPalette` (7 assets) e `FX_ESCOLHAS.md` com os candidatos. Pare.

---

## 5. Fase 3 — Arquitetura do sistema de FX

### 5.1 Níveis de intensidade

| Nível | Duração | Pode usar | Nunca | Exemplos |
|---|---|---|---|---|
| **Sussurro** | até 0,2 s | Scale Spring, Position, Rotation, som baixo | câmera, pós, partícula grande | pairar, mirar, inválida |
| **Golpe** | 0,3–0,7 s | + Position Shake ≤ 8 px no tabuleiro, partícula pequena, Floating Text, Freeze Frame ≤ 0,05 s | Bloom, Vignette | mover, capturar, equipar, reação |
| **Estrondo** | 1–2,5 s | + Flash, Freeze Frame, Vignette, Color Adjustments, Bloom em pulso ≤ 0,3 s | dois ao mesmo tempo; input livre | arcanos, condições, vitória |

Políticas:

- **Um Estrondo por vez.** Os seguintes entram em fila. Enquanto toca, o input fica bloqueado e o cursor muda para *aguardar*.
- **Repetição cansa.** Um Golpe repetido no mesmo turno toca com intensidade reduzida (padrão 0,7) e variação de som.
- **Acessibilidade:** opção *Reduzir flashes e tremores* que zera Flash e Freeze Frame e reduz tremores a 30%. Nunca mais de **3 flashes por segundo**.

### 5.2 Componentes sugeridos

Adapte ao que a Fase 0 encontrou.

- **`FxCueId`** (enum ou id estável) — um por linha do catálogo da Seção 8.
- **`FxCue`** (ScriptableObject, somente leitura em runtime): id, `FxTier`, referência ao prefab do `MMF_Player`, intensidade base, intensidade em repetição.
- **`FxScheduler`** (C# puro, testável sem Feel): fila, exclusividade de Estrondo, atenuação por repetição, liberação do lock. **Tem testes EditMode.**
- **`IFxPlayer`**: adaptador fino sobre o `MMF_Player` (`Play(posição, intensidade)`, `Stop()`, conclusão). Permite testar o scheduler com um stub.
- **`FxDirector`** (MonoBehaviour na cena): assina os eventos de jogo, resolve a posição no tabuleiro (`casa → posição de mundo`), chama o scheduler, aplica o lock de input e o cursor. API:
  - `Awaitable PlayAsync(FxCueId id, Vector3 posicao, float intensidade, CancellationToken ct)` — retorna quando o player terminar.
  - Para saber quando termina: procure no Feel instalado um método awaitable ou o evento `OnComplete` (exige *Trigger Unity Events* ligado) e converta com `AwaitableCompletionSource`. Fallback: checar `IsPlaying` por frame. Informe qual usou.
- **Feedback local de cartas e peças** (pairar, inválida, comprar, descartar, pousar): `MMF_Player`s no próprio prefab (`CardView`, `PieceView`), disparados pela view. Efeitos globais (captura, raio, bola de fogo, condições) passam pelo `FxDirector`.
- **Sequência de apresentação:** se as regras resolvem instantaneamente, a apresentação precisa tocar em ordem e o próximo turno só começa depois do Estrondo. Se o projeto ainda não tiver uma fila de apresentação, proponha uma antes de implementar.

### 5.3 Esboço de referência

Adapte nomes e namespaces e confira as APIs do Feel:

```csharp
namespace ArcaneGambit.FX
{
    public enum FxTier { Sussurro, Golpe, Estrondo }

    public interface IFxPlayer
    {
        void Play(UnityEngine.Vector3 position, float intensity);
        void Stop();
        bool IsPlaying { get; }
    }
}
```

**Testes EditMode do `FxScheduler`:**

- Estrondo bloqueia outro Estrondo até terminar.
- Sussurros não esperam na fila.
- Ordem FIFO preservada.
- Repetição no mesmo turno aplica a intensidade reduzida e é zerada na virada de turno.
- O lock de input é liberado mesmo se o player for cancelado ou destruído.

**Entrega da Fase 3:** scripts, testes rodando (mostre o resultado real), `FxDirector` na cena e um `FX_Sandbox` com painel de debug (somente Editor/Development) que dispara qualquer `FxCueId` numa casa escolhida. Pare.

---

## 6. Fase 4 — Feedback das cartas

Cada linha vira um `MMF_Player` no prefab da carta (ou no painel indicado). Durações são ponto de partida; o Rick ajusta no jogo.

| # | Evento | Nível | Feedbacks (em ordem) | ETFX | Duração |
|---|---|---|---|---|---|
| I | **Pairar na mão** | Sussurro | Scale Spring 1 → 1,1 · Position +44 px em Y · Rotation → 0° (a mão é em leque) · Sound papel. Ao sair: o mesmo player em reverso (confira `PlayFeedbacksInReverse` no Feel instalado) ou um player "Sair" | — | 0,18 s |
| II | **Jogar carta** | Golpe | Position em curva até o painel *Ação em curso* (~0,6 s) · Squash and Stretch ao pousar (1,07 / 0,93) · Scale no selo de custo 1 → 1,8 → 1 com rotação −14° → 0° ("carimbo") · TMP Count To na Moral · Sound | poeira → tinta diluída | 0,7–1,0 s |
| III | **Jogada inválida** | Sussurro | Position Shake X 8 px, 0,3 s · Image no selo: `#8E2F21` → `#C0573F` → volta, 0,5 s · Floating Text "Moral insuficiente" · Sound surdo · cursor *indisponível* | — | 0,35 s |
| IV | **Comprar** | Sussurro | Position do baralho até a mão · Rotation Y 180° → 0° (trocar verso e frente aos 90°) · Scale Spring · TMP Count To no baralho. Várias cartas: Initial Delay 0,12 s entre elas | — | 0,5–0,8 s |
| V | **Reação revelada** (carta do oponente) | Golpe | Position entra do topo com overshoot · Image Alpha no contorno vermelho, pulso 3× 0,6 s · Wiggle ±2,5° · Floating Text "Reação!" · Sound. Toca **antes** de abrir o Confronto | — | 0,6 s |
| VI | **Equipar** | Golpe | Carta: Position + Scale (→ 0,15) até o token e alfa → 0, 0,7 s · Token: Scale Spring (1 → 1,18 → 0,94 → 1) · Set Active na insígnia de equipamento no canto do token (pop 1,6 → 1) | brilho de coleta → pergaminho | 0,6 s |
| VII | **Arcano conjurado** | Estrondo | Círculo de runas (sprite) atrás da carta: Image Fill 0 → 1 em 0,6 s e rotação em loop (8 s por volta) · Scale 0,96 → 1,07 → 1,03 · contorno tracejado vermelho pulsa · brasas subindo | círculo mágico + brasas → rubrica | depende do arcano |
| VIII | **Descartar** | Sussurro | Position + Rotation 8° até a pilha · Scale 0,86 · Image com tom sépia · CanvasGroup α 0,78 · TMP Count To no descarte | — | 0,6 s |

**Entrega da Fase 4:** os oito players e as chamadas a partir das views; teste no `FX_Sandbox` e na partida. Pare.

---

## 7. Fase 5 — Ensaios principais

### 7.1 Tempestade de Relâmpagos (condição climática)

Texto da carta: *No início de cada rodada, uma casa é marcada. No fim da rodada, um raio atinge essa casa; o Soberano não pode ser alvo.*

São **três players** em momentos diferentes da partida:

**A · Chegada** — toca quando o jogador clica **Entendido** no aviso de nova condição. Estrondo, ~2,4 s.

| t (s) | O que se vê | Feedbacks | ETFX |
|---|---|---|---|
| 0,00 | O aviso fecha | CanvasGroup 1 → 0 · Scale 1 → 0,96 · 0,25 s | — |
| 0,30 | O céu fecha sobre o tabuleiro | Vignette URP 0 → 0,35 · Color Adjustments URP, exposição −0,5 | — |
| 0,40 | Nuvens de tinta descem do topo | Particles Instantiation | nuvem/fumaça → tinta 50% |
| 0,90 | Chuva fina em loop | Particles Play | chuva → traço de tinta 35% |
| 1,40 | Trovão distante | Flash 0,08 s, α 0,3 · Sound | raio pequeno ao fundo |
| 1,90 | A carta vai para *Condições em campo* | Position · Scale Spring · Sound | — |

**B · Casa marcada** — início de cada rodada. Golpe, ~1,2 s.

| t (s) | O que se vê | Feedbacks | ETFX |
|---|---|---|---|
| 0,00 | Nuvem adensa sobre a casa | Particles Instantiation na casa | fumaça pequena → tinta |
| 0,30 | Anel tracejado vermelho se crava na casa | Scale 1,6 → 1 · Flicker em loop até o raio | — |
| 0,70 | A peça na casa (se houver) estremece | Wiggle em loop · Floating Text "d4 marcada" · Sound | — |

**C · Raio** — fim de cada rodada. Estrondo, ~1,8 s.

| t (s) | O que se vê | Feedbacks | ETFX |
|---|---|---|---|
| 0,00 | Antecipação | Vignette URP em pulso · Scale 1 → 1,02 no tabuleiro, centrado na casa | — |
| 0,25 | O raio | Particles Instantiation · Flash 0,06 s · Freeze Frame 0,08 s · Bloom URP em pulso | raio → núcleo pergaminho, halo rubrica |
| 0,35 | Tremor | Position Shake no tabuleiro, 12 px, 0,35 s · Sound · Haptic | — |
| 0,50 | Impacto; a peça vira borrão | Squash and Stretch → Set Active · Instantiate Object (mancha de tinta na casa) | explosão pequena + respingos → tinta |
| 0,95 | Resultado | Floating Text "Raio! Infantaria capturada" · o anel some | — |

Regras:

- **Casa vazia:** o mesmo player C com intensidade 0,6 e sem captura.
- **Ambiente enquanto a condição durar:** chuva a 40%, Vignette 0,2 e um trovão distante a cada 8–14 s (player curto disparado por timer aleatório; sem flash forte).
- **Removida (Prece ao Amanhecer):** player de saída que para a emissão, sobe as nuvens e devolve Vignette e Color Adjustments a 0.

### 7.2 Bola de Fogo (Arcanismo · Ritual)

Texto: *declare captura contra Infantaria ou Besteiros inimigos a até 2 casas de uma unidade aliada. Não atinge Soberano nem Engenharia; permite Reação.*

Quatro players encadeados pelo script da carta:

**1 · Conjuração** — ao confirmar o alvo.

| t (s) | O que se vê | Feedbacks | ETFX |
|---|---|---|---|
| 0,00 | Carta vai para *Ação em curso* | Position · Scale Spring · Sound | — |
| 0,10 | Círculo de runas sob a unidade aliada | Scale 0,4 → 1 · Rotation em loop | círculo mágico → rubrica |
| 0,30 | Brasas sobem; a unidade se apruma | Particles Play em loop · Scale Spring no token | brasas → rubrica |
| 0,60 | Linha de mira tracejada até o alvo; anel no alvo | Set Active · Scale 1,5 → 1 no anel · Flicker | — |

O player termina num **Looper** (ou Holding Pause): o círculo e as brasas ficam vivos **enquanto o oponente decide a Reação** (a janela de Confronto abre por cima). Cursor *aguardar*.

**2 · Disparo** — quando a reação resolve. ~0,55 s.

- Particles Instantiation do projétil (fogo → núcleo pergaminho, corpo rubrica, rastro de tinta).
- Trajetória **em arco**, da unidade aliada ao alvo: Position com curva (ou script, se o Position do Feel não fizer arco).
- Sound.

**3a · Impacto — captura**

| t após o impacto (s) | O que se vê | Feedbacks | ETFX |
|---|---|---|---|
| 0,00 | Explosão | Particles Instantiation · Flash 0,05 s · Freeze Frame 0,06 s · Bloom URP em pulso | explosão de fogo → rubrica e tinta |
| 0,05 | Tremor | Position Shake no tabuleiro, 8 px, 0,3 s · Sound · Haptic | — |
| 0,15 | A peça vira borrão e fumaça | Squash and Stretch → Set Active · Instantiate Object (mancha) | fumaça → tinta diluída |
| 0,50 | Resultado | Floating Text "Capturada" | — |

**3b · Impacto — bloqueio (Erguer Escudos)**

| t após o impacto (s) | O que se vê | Feedbacks | ETFX |
|---|---|---|---|
| −0,20 | Escudo de tinta surge entre o projétil e o alvo | Scale Spring 0 → 1 · Sound de metal | — |
| 0,00 | A bola estoura no escudo | Particles Instantiation · Rotation Shake no escudo · Flash α 0,35 | faíscas → tinta e pergaminho |
| 0,05 | O alvo recua e fica | Position (recuo) · Position Shake 3 px | fumaça → tinta diluída |
| 0,50 | Resultado | Floating Text "Bloqueada · Erguer Escudos" | — |

O player **3b é genérico**: serve para qualquer Reação de escudo contra ataque à distância (Salva de Virotes etc.).

**Entrega da Fase 5:** os players acima, as chamadas reais a partir da lógica das cartas e um botão no `FX_Sandbox` para cada um. Pare.

---

## 8. Fase 6 — Catálogo completo

Implemente na ordem: **A → B → D → E → C → F**. Na coluna Feel, os feedbacks estão em ordem; na coluna ETFX, o tipo de efeito a procurar (ver `FX_ESCOLHAS.md`).

### A · Cartas

| Evento | Nível | Feel | ETFX | Duração |
|---|---|---|---|---|
| Pairar na mão | Sussurro | Scale Spring · Position · Rotation · Sound — reverso ao sair | — | 0,18 s |
| Jogar carta | Golpe | Position em curva · Squash and Stretch · Scale no selo · TMP Count To (Moral) · Sound | poeira → tinta diluída | 0,7 s |
| Jogada inválida | Sussurro | Position Shake 8 px · Image no selo · Floating Text · Sound | — | 0,35 s |
| Comprar | Sussurro | Position · Rotation em Y · Scale Spring · TMP Count To (baralho) | — | 0,5 s |
| Descartar | Sussurro | Position + Rotation até a pilha · Image em sépia · CanvasGroup · TMP Count To | — | 0,45 s |
| Reação revelada | Golpe | Position · Image Alpha em loop · Wiggle · Floating Text · Sound | — | 0,6 s |

### B · Peças

| Evento | Nível | Feel | ETFX | Duração |
|---|---|---|---|---|
| Mover | Sussurro | Position em arco curto · Squash and Stretch ao pousar · Sound | poeira no pouso → tinta diluída | 0,3 s |
| Salto da Cavalaria | Sussurro | Position em arco alto · Squash and Stretch · Sound | poeira → tinta diluída | 0,4 s |
| Recrutar da reserva | Golpe | Position desde a reserva · Scale Spring · TMP Count To (reserva) · Sound | poof → tinta diluída | 0,5 s |
| Captura por movimento | Golpe | Squash and Stretch · Freeze Frame 0,04 s · Position Shake 4 px · Set Active · Sound | impacto → tinta + borrão | 0,6 s |
| Captura bloqueada | Golpe | Scale Spring no escudo · Rotation Shake · Position Shake 3 px · Floating Text | faíscas → tinta e pergaminho | 0,6 s |
| Equipar | Golpe | Position + Scale até o token · Scale Spring · Set Active (insígnia) | brilho de coleta → pergaminho | 0,6 s |
| Ganhar Moral | Sussurro | TMP Count To · Scale Spring no selo · Floating Text "+I" | brilho pequeno → rubrica | 0,4 s |

### C · Táticas, Suprimentos e Fortificações

| Evento | Nível | Feel | ETFX | Duração |
|---|---|---|---|---|
| Salva de Virotes | Golpe | Particles Instantiation em rajada · Position Shake no alvo · Sound | projéteis pequenos → tinta | 0,8 s |
| Marcha Forçada · Finta · Avanço | Sussurro | Position com rastro · Scale Spring | rastro → tinta diluída | 0,4 s |
| Cortina de Fumaça | Golpe | Particles Play em loop · Image Alpha nas casas cobertas | fumaça → tinta diluída 50% | 0,8 s + loop |
| Paliçada · Estacas · Fosso | Golpe | Scale Spring a partir de 0 · Squash and Stretch · Sound de madeira/terra | poeira e terra → tinta diluída | 0,6 s |
| Carroça · Rações · Batedores | Sussurro | Igual a Comprar, Initial Delay 0,12 s por carta | — | 0,5 s + 0,12 s/carta |

### D · Arcanismo

| Evento | Nível | Feel | ETFX | Duração |
|---|---|---|---|---|
| Bola de Fogo | Estrondo | Seção 7.2 | projétil de fogo + explosão → rubrica e tinta | 0,9 s + reação + 1,7 s |
| Presságio do Vidente | Golpe | Position · Rotation (três cartas viram) · CanvasGroup · Sound | brilho mágico → rubrica | 0,9 s |
| Prece ao Amanhecer | Estrondo | Saída da condição ativa · Flash suave pergaminho · Color Adjustments e Vignette voltam a 0 | raios de luz → pergaminho | 1,6 s |
| Selo da Vigília | Golpe | Scale em carimbo 1,6 → 1 · Flash rubrica α 0,3 · Position Shake 3 px | anel de impacto → rubrica | 0,7 s |
| Eco do Oráculo | Golpe | Position do descarte ao baralho · Rotation · Sound | rastro mágico → rubrica | 0,8 s |

### E · Condições climáticas (entrada após *Entendido* + ambiente em loop)

| Evento | Nível | Feel | ETFX | Duração |
|---|---|---|---|---|
| Nevasca | Estrondo | Color Adjustments URP saturação −30 · Vignette 0,2 · Particles Play em loop | neve → pergaminho com contorno | 1,8 s + loop |
| Ventania | Estrondo | Particles Play em loop · Wiggle nos estandartes · projéteis desviam no ar (Position com ruído) | vento, folhas, poeira → tinta diluída | 1,4 s + loop |
| Neblina | Estrondo | Vignette 0,3 · Particles Play em loop rasteiro | névoa → pergaminho 40% | 1,6 s + loop |
| Lamaçal | Golpe | Instantiate Object (manchas nas casas) · Squash and Stretch pesado ao mover | respingos → tinta | 1,0 s |
| Noite Sem Lua | Estrondo | Color Adjustments URP exposição −0,8 · Vignette 0,45 · Flicker nas tochas | brasas de tocha → rubrica | 1,6 s + loop |
| Tempestade de Relâmpagos | Estrondo | Seção 7.1 | nuvem, chuva, raio → tinta e pergaminho | 2,4 s · marca 1,2 s · raio 1,8 s |
| Condição removida | Golpe | Player de saída: para a emissão, devolve Color Adjustments e Vignette | — | 1,0 s |

Mais de uma condição pode ficar ativa ao mesmo tempo. Os efeitos de pós-processo **não podem se sobrescrever**: some ou combine os valores (por exemplo, a Vignette final é o maior valor entre as condições ativas) e documente a regra escolhida.

### F · Partida

| Evento | Nível | Feel | ETFX | Duração |
|---|---|---|---|---|
| Início de partida | Golpe | CanvasGroup · Position (exércitos entram por fileira com Initial Delay) · Sound | — | 1,2 s |
| Novo turno | Sussurro | Position da faixa · TMP Text Reveal · Sound | — | 0,6 s |
| Vitória | Estrondo | Freeze Frame · Scale Spring no selo · Flash rubrica · Sound | brilho e pétalas → rubrica e pergaminho | 2,0 s |
| Derrota | Golpe | Color Adjustments saturação −60 · Vignette 0,4 · CanvasGroup | fumaça → tinta | 1,6 s |

---

## 9. Critérios de aceite (todas as fases)

- [ ] Nenhum arquivo original do Feel ou do ETFX foi modificado; todos os prefabs usados terminam em `_AG`.
- [ ] Nenhuma cor fora da paleta da Seção 4.1 aparece nos efeitos; Bloom só em pulsos ≤ 0,3 s.
- [ ] Só um Estrondo toca por vez; input bloqueado e cursor *aguardar* durante ele; lock sempre liberado (inclusive se a cena trocar no meio).
- [ ] A HUD não treme; o tabuleiro sim.
- [ ] *Reduzir flashes e tremores* funciona; nunca mais de 3 flashes por segundo.
- [ ] Partículas em pool, sem picos de alocação ao capturar ou ao lançar raio. Verifique no Profiler, com números.
- [ ] Testes EditMode do `FxScheduler` passando. Mostre o resultado real.
- [ ] Cada cue pode ser disparado no `FX_Sandbox`.
- [ ] Sem erros nem warnings novos no Console.

## 10. Fora de escopo agora

- **Áudio final.** Deixe os feedbacks Sound com slot vazio ou placeholder. Será a próxima etapa; o ETFX inclui efeitos sonoros que podem ser usados.
- **Shaders customizados** (contorno de tinta em tempo real, papel animado). Só proponha se a recoloração não bastar.
