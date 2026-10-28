# Arcane Gambit — Game Design Document (GDD)

> **Versão:** v0.2  
> **Status:** Documento conceitual / brainstorm — sujeito a alterações  
> **Data:** 27 de outubro de 2026  
> **Gênero:** Estratégia tática por turnos, combate em tabuleiro e construção de baralhos  
> **Plataforma inicial proposta:** PC (Steam)  
> **Tecnologia prevista:** Unity / C#

---

## 1. Visão geral

**Arcane Gambit** é um jogo de estratégia militar medieval que combina a linguagem espacial do xadrez com cartas de equipamentos, táticas, suprimentos e reações, inspiradas em jogos de cartas estratégicos como *Magic: The Gathering* e *Hearthstone*.

Cada jogador assume o comando de um exército, protege seu **Soberano**, mobiliza novas unidades ao longo da partida e administra um recurso compartilhado chamado **Moral**. O posicionamento determina quais capturas podem ser tentadas; as cartas podem alterar o resultado de confrontos, proteger peças, permitir manobras excepcionais e modificar temporariamente as condições do tabuleiro.

O jogo não deve ser percebido como um duelo de magos com peças de xadrez, mas como **um confronto militar medieval em que a magia é rara, discreta e extraordinária**.

### Proposta de valor

> O posicionamento determina quem pode atacar. As cartas determinam como o confronto se resolve. A Moral determina quais opções permanecem disponíveis.

### Objetivos de experiência

- Oferecer partidas legíveis, tensas e centradas em decisões táticas.
- Preservar movimentos reconhecíveis do xadrez, sem obrigatoriamente copiar todas as suas regras.
- Utilizar cartas como **exceções táticas**, e não como substitutas do posicionamento.
- Incentivar gestão de risco: investir Moral em reforços agora ou guardá-la para reagir no turno inimigo.
- Valorizar formações, proteção do Soberano, sacrifícios, blefes e controle territorial.
- Evitar excesso de aleatoriedade, números de dano e efeitos mágicos espetaculares.

## 2. Identidade temática e direção artística

**Ambientação:** fantasia medieval de baixa magia (*low fantasy*), inspirada em campanhas militares, fortalezas, cavalaria, armas de cerco, heráldica e logística de exércitos.

**Elementos predominantes:** infantaria, lanceiros, cavaleiros montados, arqueiros, besteiros, fortificações, estandartes, armaduras, armas e suprimentos.

**Elementos arcanos:** relíquias, presságios, bênçãos, vidência e fenômenos pouco frequentes. Magia deve ser excepcional, não o principal repertório de combate.

**Direção visual proposta:** mesa de guerra ou campo de batalha estilizado, com peças/unidades de leitura imediata, iconografia medieval, cartas semelhantes a ordens e itens militares e efeitos visuais curtos.

**Público-alvo provisório:** jogadores de xadrez casual, jogos táticos por turnos e card games estratégicos.

**Duração-alvo não validada:** aproximadamente 10–15 minutos por partida.

## 3. Tabuleiro e condição de vitória

- **Tabuleiro-base proposto:** 8 × 8 casas.
- **Jogadores:** dois exércitos em lados opostos.
- **Condição principal de vitória:** realizar e resolver com sucesso uma captura do Soberano inimigo.
- **Xeque e xeque-mate tradicionais:** não são aplicados literalmente na v0.2. O Soberano pode estar ameaçado e ser salvo por uma Reação legal antes da resolução da captura.
- **Capturas convencionais:** não exigem rolagens, porcentagens de acerto nem pontos de Vida. Uma peça é capturada ou permanece no tabuleiro.
- **Objetivos secundários:** controle territorial e linhas de suprimento são possibilidades futuras, ainda não formalizadas.

### 3.1. Formação inicial — proposta preferencial

Cada jogador começa com:

- 1 **Soberano**;
- 2 unidades de **Infantaria**;
- 0 pontos de Moral em reserva, recebendo **+2 Moral na Preparação de seu primeiro turno**.

Um arranjo experimental seria: Soberano em e1/e8 e Infantarias em d2/f2 e d7/f7, respectivamente. O restante do exército permanece em **reserva** e pode ser mobilizado durante a partida.

**Alternativas ainda em discussão:** começar apenas com o Soberano; ou permitir uma fase de implantação prévia usando um orçamento de recrutamento. A formação com escolta é a hipótese favorita, **não uma decisão definitiva**.

### 3.2. Recrutamento e zona de implantação

- As unidades são recrutadas a partir de uma **reserva militar**, separada do baralho de cartas.
- O jogador paga Moral para colocar uma peça no tabuleiro.
- Recrutar consome uma **Ordem**.
- Na proposta inicial, reforços surgem em casas livres das duas primeiras fileiras do próprio lado.
- Unidades recrutadas não se movimentam nem atacam no turno em que entram.
- Implantação avançada via postos de suprimento permanece como ideia futura.

## 4. Hierarquia militar das peças

| Peça de referência | Unidade de Arcane Gambit | Papel tático | Custo de Moral sugerido |
|---|---|---|---:|
| Peão | Infantaria | Proteção, ocupação, formações | 1 |
| Cavalo | Cavalaria | Mobilidade, cargas e flanqueamentos | 3 |
| Bispo | Arqueiros / Besteiros | Controle de diagonais e cobertura | 2 |
| Torre | Engenharia de Guerra | Equipamentos de cerco e posições defensivas | 4 |
| Dama/Rainha | Marechal / Guarda Real | Comando, elite e ofensivas | 5 |
| Rei | Soberano | Liderança e objetivo central | Inicial; não recrutável em regra |

Os custos são **hipóteses de balanceamento**. A composição exata da reserva, limites por classe e variantes de unidade ainda serão definidos.

### 4.1. Movimento e captura — hipótese de trabalho

A v0.2 considera **movimentos inspirados no xadrez**, mas com adaptações à identidade medieval. Os padrões abaixo **não estão fechados**:

| Unidade | Movimento candidato | Captura convencional |
|---|---|---|
| Infantaria | 1 casa para a frente | 1 casa na diagonal dianteira, como o peão |
| Cavalaria | Salto em L, como o cavalo | Na casa de destino do salto |
| Arqueiros / Besteiros | Até 2 casas na diagonal, sem atravessar peças | Ocupando a casa adversária dentro do alcance de movimento; disparo à distância depende de carta |
| Engenharia de Guerra | 1 casa em linha reta por ação | Em linha reta, dentro do alcance permitido; alcance maior pode depender de cartas |
| Marechal | Até 2 casas em linha reta ou diagonal | Na casa de destino |
| Soberano | 1 casa em qualquer direção | Na casa de destino; sujeito a reações especiais |

**Regra-base:** numa captura comum, a peça atacante tenta ocupar a casa da unidade inimiga; se a captura se concretizar, a peça defensora é retirada.

**Exceção prevista:** cartas específicas permitem **ataques à distância sem deslocar o atacante**, sobretudo com Arqueiros/Besteiros e Engenharia de Guerra.

**Regras tradicionais ainda indefinidas:** roque, en passant, promoção da Infantaria, repetição de posições, empates, restrições especiais de movimento do Soberano e outros casos de borda.

## 5. Sistema de Moral

A **Moral** representa a capacidade do comandante de mobilizar forças, executar ordens especiais e preparar respostas defensivas. É um recurso compartilhado por recrutamento e cartas.

| Parâmetro | Proposta v0.2 |
|---|---|
| Moral antes do primeiro turno | 0 |
| Recebimento na Preparação | +2 por turno próprio |
| Capacidade máxima de armazenamento | 8 |
| Persistência entre turnos | Sim |
| Custo de uma captura convencional | 0 Moral; consome 1 Ordem |
| Custo de cartas de Reação | Em geral 1–2 Moral |
| Custo de cartas ofensivas/táticas | Em geral 1–3 Moral |
| Recompensa automática de Moral por captura | **Não prevista** inicialmente |

**Tensão econômica central:** gastar Moral em Cavalaria ou Marechal aumenta a presença militar, mas diminui a capacidade de defender peças durante o turno adversário. A Moral poupada permite blefes e reações.

**Risco de balanceamento:** aumentar os recursos do jogador já dominante pode intensificar o chamado *snowball*. Bônus por capturas e por controle territorial devem ser testados com cuidado.

## 6. Ordens e fases do turno

**Limite inicial proposto:** 2 **Ordens militares** por turno próprio.

Cada uma das seguintes ações consome 1 Ordem:

1. Recrutar uma unidade da reserva.
2. Movimentar uma unidade já presente no tabuleiro.
3. Declarar uma tentativa de captura com uma unidade.

A mesma unidade pode eventualmente participar de mais de uma ação no turno, respeitando os movimentos válidos e restrições específicas; este ponto ainda precisa de testes.

### 6.1. Preparação

- Receber +2 Moral, respeitando o máximo de 8.
- Comprar 1 carta.
- Resolver efeitos de começo de turno.

### 6.2. Recrutamento e Manobras

- Gastar até 2 Ordens em recrutamento, movimentação ou tentativa de captura.
- Usar, como proposta inicial, **até 1 carta proativa** por turno (Tática, Equipamento, Suprimento ou Fortificação), salvo texto específico da carta.
- Cartas proativas não consomem Ordem, exceto quando seu efeito disser o contrário.

### 6.3. Janela de confronto (interrupção)

- Abre-se imediatamente quando é declarada uma captura válida.
- O defensor pode jogar **até 1 carta de Reação** aplicável, pagando sua Moral.
- O atacante não responde à Reação no primeiro protótipo.
- Proteções de equipamento já existentes são verificadas durante a resolução; a interação entre equipamento e uma Reação adicional precisa ser formalizada nos testes.

### 6.4. Encerramento

- Resolver efeitos que expirem no fim do turno.
- Ajustar o tamanho da mão, se houver limite.
- Passar o controle ao adversário.

**Nota:** a Janela de Confronto não concede ataques gratuitos. Ela interrompe temporariamente a fase de Manobras para resolver a tentativa de captura declarada.

## 7. Modelo de combate: captura com reações

A v0.2 **adota como modelo prioritário de prototipagem as capturas instantâneas com cartas de reação**, em vez de Vida/HP e dano numérico.

### 7.1. Procedimento de uma captura

1. **Declaração:** o atacante escolhe um alvo que sua unidade pode capturar pelas regras atuais.
2. **Verificação:** confirma-se que a ação é válida e há Ordem disponível; a Ordem é consumida.
3. **Reação:** o defensor pode optar por uma carta de Reação, caso tenha Moral e condições para jogá-la.
4. **Resolução:** aplicam-se os efeitos defensivos válidos e resolve-se a tentativa de captura.
5. **Resultado:** uma das situações abaixo acontece.

### 7.2. Resultados possíveis

| Resultado | Peça atacante | Peça defensora | Efeito estratégico |
|---|---|---|---|
| Captura bem-sucedida | Ocupa a casa atacada | É removida | Eliminação e ganho de posição |
| Captura bloqueada | Permanece na origem | Permanece na casa original | Atacante perde a Ordem; defesa consome recurso |
| Retirada defensiva | Avança para a casa abandonada | Recuar para uma casa válida e vazia | Defensor preserva a peça, mas cede território |
| Sacrifício/interceptação | Resolve captura contra substituto | Unidade protetora é removida | Peça de maior valor sobrevive |
| Disparo à distância especial | Permanece na origem | É removida se não houver defesa | Eliminação sem avanço; exige carta específica |

**Proteções não devem ser infinitas.** Escudos, armaduras e outras defesas persistentes terão usos limitados, duração definida ou condições claras de ativação.

### 7.3. Exemplos de confronto

**A. Cavalaria contra Infantaria**

- Cavaleiro branco em **f3** tenta capturar Infantaria preta em **e5** por salto em L.
- Sem Reação: a Infantaria é removida e o Cavaleiro ocupa e5.
- Com **Erguer Escudos** (1 Moral): a captura é cancelada; ambas permanecem nas casas originais.
- Com **Retirada Organizada** (2 Moral): a Infantaria recua de e5 para e6, **se a casa estiver livre e o recuo for legal**, e o Cavaleiro avança para e5.

**B. Engenharia de Guerra com blindagem**

- Uma unidade de Engenharia de Guerra é alvo de uma captura convencional válida.
- Possui **Blindagem Reforçada** com uma carga de proteção.
- A blindagem é consumida e a tentativa de captura é bloqueada; o atacante perde a Ordem e permanece onde estava.
- Uma tentativa posterior poderá capturá-la caso não exista nova proteção.

**C. Besteiros e disparo especial**

- Besteiros brancos em **c4** e Cavalaria preta em **f7**, sem obstruções nas casas d5/e6.
- A carta **Salva de Virotes** permite atacar em diagonal a até 3 casas, sem deslocar os Besteiros.
- A captura é declarada e pode receber uma Reação defensiva comum.
- Se resolvida com sucesso, a Cavalaria é removida e os Besteiros permanecem em c4. A carta **não pode atingir o Soberano**.

**D. Sacrifício para proteger o Soberano**

- Em uma posição intermediária, Cavaleiro branco em **f6** ameaça capturar Soberano preto em **g8**, protegido por Infantaria preta em **f8**.
- O defensor utiliza **Guarda do Soberano** (2 Moral).
- O Soberano troca de casa com a Infantaria: vai para f8, enquanto ela vai para g8.
- A captura é resolvida contra a Infantaria, que é removida. O Soberano sobrevive.
- Deve-se validar que a casa de destino do Soberano e a nova posição resultante sejam permitidas pelas regras específicas desse efeito.

## 8. Cartas e construção do baralho

O baralho representa um **arsenal de recursos militares**, não uma coleção de invocações mágicas. As peças são recrutadas da reserva, não compradas como cartas de unidade no modelo preferencial.

### 8.1. Categorias

- **Equipamentos:** armas, escudos, proteções, armaduras e modificações persistentes em unidades.
- **Táticas militares:** cargas, marchas, reposicionamentos, ataques especiais e ordens de batalha.
- **Reações:** bloqueios, retiradas, interceptações e contramedidas durante capturas inimigas.
- **Suprimentos e fortificações:** rações, paliçadas, reforços logísticos e preparo de terreno.
- **Arcano (raro):** presságios, relíquias, bênçãos e habilidades discretas de caráter sobrenatural.

### 8.2. Tamanho e composição de referência

**Deck experimental:** 24 cartas. Distribuição ilustrativa:

| Categoria | Quantidade |
|---|---:|
| Equipamentos | 7 |
| Táticas militares | 6 |
| Reações | 5 |
| Suprimentos e fortificações | 4 |
| Arcano | 2 |
| **Total** | **24** |

A composição é uma **referência temática, não uma regra obrigatória do deckbuilder**. Para o MVP, usar dois baralhos predefinidos com cerca de 12 cartas únicas no total ou um conjunto reduzido de 12–16 cartas únicas.

**Mão inicial sugerida:** 4 cartas, com compra de 1 carta por turno. Mulligan, limite de mão, limite de cópias e reciclagem do descarte continuam abertos.

### 8.3. Cartas-exemplo (textos provisórios)

| Carta | Tipo | Moral | Efeito proposto |
|---|---|---:|---|
| Erguer Escudos | Reação | 1 | Cancela uma captura contra Infantaria |
| Retirada Organizada | Reação | 2 | Reposiciona a unidade atacada uma casa legal para a retaguarda; o atacante ocupa a casa abandonada |
| Lanças Preparadas | Reação | 1 | Cancela uma captura de Cavalaria contra Infantaria |
| Guarda do Soberano | Reação | 2 | Uma Infantaria adjacente intercepta a captura dirigida ao Soberano |
| Escudo Reforçado | Equipamento | 2 | Cancela a próxima captura contra a unidade equipada e então é descartado |
| Blindagem Reforçada | Equipamento | 2 | Concede uma carga de proteção a Engenharia de Guerra |
| Martelo de Guerra | Equipamento | 2 | Permite ignorar uma carga de proteção defensiva durante uma captura |
| Rações Extras de Viagem | Suprimento | 1 | Após um movimento, permite 1 passo adicional para uma casa adjacente livre e válida |
| Marcha Forçada | Tática | 2 | Permite um movimento adicional, sujeito às restrições da carta |
| Salva de Virotes | Tática | 2 | Besteiros tentam capturar a até 3 casas em diagonal sem avançar; não afeta o Soberano |
| Paliçada Improvisada | Fortificação | 2 | Cria um obstáculo defensivo temporário numa casa válida |
| Presságio do Vidente | Arcano | 3 | Olha as próximas 3 cartas do deck e compra 1 delas; destino das demais a definir |

### 8.4. Equipamentos e limites

- Equipamentos devem possuir duração, cargas ou durabilidade explícitas.
- Sugere-se um máximo de **1 equipamento por categoria por unidade** (arma, proteção, acessório).
- A expressão **“+1”** em um item representa, preferencialmente, uma carga, melhoria de capacidade ou efeito definido — **não pontos de Vida**, já que o modelo atual não utiliza HP.
- A interação entre equipamentos que bloqueiam capturas e cartas de Reação ainda requer uma regra de prioridade.

## 9. Papel especial do Soberano

O Soberano representa a liderança do exército e a condição central de vitória.

**Regras já adotadas como direção:**

- Começa no tabuleiro.
- Sua captura bem-sucedida encerra a partida.
- Pode ser protegido por formações, unidades adjacentes e cartas de Reação.
- Não é uma tropa comum recrutável.

**Habilidades em estudo:** Inspiração Real (vantagens às unidades próximas), Ordem do Soberano (Ordem extra limitada) e Guarda Real (proteção por unidades adjacentes). A v0.2 **não fixa uma habilidade passiva obrigatória** para evitar conflitos com o sistema sem Vida.

## 10. Mecânicas de diferenciação em estudo

Estas ideias fazem parte da visão, mas **não são regras fechadas da v0.2**:

1. **Formações militares:** unidades adjacentes ativam escudos coletivos, linhas de lanceiros e posições protegidas.
2. **Linhas de suprimento:** controle territorial habilita recrutamento ou reforços em áreas avançadas.
3. **Equipamentos persistentes:** melhorias com cargas ou durabilidade transformam peças comuns em unidades especializadas.
4. **Blefe e informação incompleta:** Moral visível e cartas ocultas geram decisões sob risco.
5. **Magia discreta:** cartas arcanas raras alteram informação, previsão ou condições específicas de combate.
6. **Variantes de classe:** lanceiros/espadachins, arqueiros/besteiros, diferentes máquinas de guerra ou estilos de Marechal.
7. **Tipos de Soberano:** perfis militares que alteram sinergias do exército.

## 11. Riscos e hipóteses de balanceamento

| Risco | Medida de mitigação |
|---|---|
| Reações anulam capturas em excesso e prolongam partidas | Uma Reação por confronto, custos reais e defesas consumíveis |
| Jogador dominante acumula vantagens econômicas | Não conceder Moral automática por captura; testar com cautela bônus territoriais |
| Cartas tornam impossível ler ameaças | Movimentos-base simples, destaque de casas ameaçadas e efeitos com texto claro |
| Acúmulo de escudos cria invulnerabilidade | Limitar cargas, categorias de equipamento e prioridade de efeitos |
| O Soberano fica fácil ou impossível de capturar | Testar proteção por escolta e cartas específicas; sem imunidade permanente |
| Muitas tropas travam um sistema de só 2 Ordens | Medir tempo de turno e testar 2–3 Ordens conforme tamanho do exército |
| Disparos à distância dominam o combate | Restringir alcance, classes aptas, custo de Moral e uso por cartas |
| Aleatoriedade do deck decide a partida | Garantir recrutamento independente das cartas e variedade de reações básicas |

**Hipótese prioritária a validar:** uma captura bloqueada ainda deve provocar consequência estratégica (gasto de Moral, consumo de carta/equipamento, perda de Ordem ou concessão territorial).

## 12. Protótipo jogável (MVP de regras)

### Incluído

- Tabuleiro 8 × 8 e dois jogadores locais no mesmo dispositivo.
- Soberano e duas Infantarias no início para cada lado.
- Reserva de peças e recrutamento por Moral.
- Movimentações e capturas legais das classes escolhidas.
- Economia de Moral persistente e 2 Ordens por turno.
- Sistema de cartas com baralho, mão, descarte, custo e efeitos.
- Janela de Reação para tentativas de captura.
- Ao menos três resultados de confronto: capturar, bloquear e recuar.
- 2 baralhos predefinidos; cerca de 12–16 cartas únicas.
- Captura do Soberano como condição de vitória.
- Histórico de jogadas e indicação visual de ameaças.

### Fora do MVP inicial

- PvP online, matchmaking e ranking.
- Monetização, colecionáveis e loja.
- Campanha, IA adversária sofisticada e progressão metajogo.
- Facções numerosas, dezenas de variantes por classe e efeitos arcanos elaborados.
- Simulação de Vida/dano e cálculos de precisão probabilística.

### Metas de teste

- Entender se as reações tornam as capturas mais interessantes sem causar paralisia.
- Medir duração de partidas e de janelas de confronto.
- Verificar se a escolha entre recrutar e guardar Moral é realmente difícil.
- Validar se as seis classes têm identidades distintas e úteis.
- Verificar se ataques à distância por cartas são legíveis e equilibráveis.

## 13. Direção técnica preliminar

**Engine:** Unity com C#.

**Arquitetura recomendada:** separar regras de jogo da apresentação visual.

- **Domain / Rules Engine:** estado do tabuleiro, movimentos legais, regras de captura, Moral, Ordens, efeitos, condição de vitória.
- **Application:** gerenciamento de turnos, validação de comandos, execução de ações e histórico.
- **Presentation (Unity):** tabuleiro, seleção, animações, cartas, HUD e indicadores de ameaça.
- **Infrastructure:** armazenamento, serialização, replays e futura integração de rede.

**Princípio técnico:** ações determinísticas por comandos (`RecruitUnit`, `MoveUnit`, `DeclareCapture`, `PlayCard`, `ResolveCombat`), com registro de eventos para testes, replay e possível sincronização futura.

**Dados de cartas:** podem ser descritos com `ScriptableObjects` ou definições de dados, mas a resolução de efeitos deve ser desacoplada de `MonoBehaviour` e testável isoladamente.

## 14. Decisões em aberto para a v0.3

1. **Movimentos definitivos:** quanto preservar do xadrez clássico para Bispo, Torre e Dama?
2. **Movimento versus ataque:** captura consome sempre uma Ordem distinta do movimento? Como se comportam ações consecutivas da mesma unidade?
3. **Equipamento e reação:** um bloqueio pré-equipado pode coexistir com uma carta de Reação no mesmo confronto?
4. **Tabuleiro inicial:** escolta fixa, Soberano sozinho ou compra de formação inicial?
5. **Unidades da reserva:** quantidades máximas de cada classe e disponibilidade de substituições.
6. **Cartas:** regra de mulligan, limite de mão, número de cópias e embaralhamento do descarte.
7. **Empates e antitravamento:** limite de turnos, repetição e critérios de desempate.
8. **Condições do Soberano:** validade de movimentos para casas ameaçadas e prioridade de efeitos de interceptação.
9. **Ataques à distância:** quais classes, distâncias e obstáculos; quais cartas podem afetar o Soberano?
10. **Exércitos e decks:** construção independente de ambos ou exércitos predefinidos vinculados a arquétipos?
11. **Informação:** visibilidade da Moral, cartas reveladas, equipamentos visíveis e log de efeitos.
12. **Duração-alvo:** validar se 10–15 minutos é compatível com as fases, o tamanho do tabuleiro e as reações.

## 15. Histórico de evolução

| Versão | Foco | Situação |
|---|---|---|
| v0.1 | Conceito de xadrez + cartas, reserva de tropas, magia e deckbuilding | Exploração inicial |
| **v0.2** | **Exército medieval, Moral, Ordens, captura instantânea com janela de Reação, arsenal militar e baixa magia** | **Versão atual do brainstorm** |
| v0.3 | Formalizar movimentos, resolução de efeitos, casos de borda e regras testáveis | Próxima etapa proposta |

---

> **Importante:** Este GDD v0.2 é uma **especificação conceitual em evolução**, não um conjunto completo e fechado de regras pronto para implementação. Custos, movimentações, tamanhos de baralho, limites e efeitos registrados como *propostas*, *hipóteses* ou *em estudo* precisam de validação em protótipo e testes de jogo.
