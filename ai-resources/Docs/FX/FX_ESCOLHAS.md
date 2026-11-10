# Arcane Gambit — Escolha dos efeitos do Epic Toon FX

Cada linha é uma **variante `_AG`**: um prefab do Epic Toon FX recolorido para a paleta do manuscrito. Uma variante serve a vários eventos do catálogo (FX_SPEC, Seção 8). A camada e o tamanho podem ser ajustados por evento na Fase 3; aqui vale o padrão.

## Como escolher

1. Importe o Epic Toon FX e, em seguida, `Assets/Epic Toon FX/Upgrade/ETFX URP Upgrade (6000.0.11f1).unitypackage`.
2. Veja os candidatos rodando as cenas de `Assets/Epic Toon FX/Demo/Scenes`. As mais úteis aqui: `etfx_explosions`, `etfx_explosions2`, `etfx_fire`, `etfx_lightning`, `etfx_magic`, `etfx_magic2`, `etfx_magic3`, `etfx_wind`, `etfx_missiles`, `etfx_combat`, `etfx_sparkles`, `etfx_2ddemo` e `Extras/etfx_library`. Também dá para arrastar o prefab direto numa cena.
3. Pense na forma e no movimento, não na cor: a ferramenta troca todas as cores pela paleta.
4. Copie um caminho da coluna **Candidatos** para **Escolhido**. Qualquer outro prefab em `Assets/Epic Toon FX/` também serve; o caminho é relativo a essa pasta, sem `.prefab`.
5. Ajuste, se quiser, **Paleta**, **Camada** (`Tabuleiro` ou `Interface`), **Escala** e **Alfa**. Vírgula ou ponto como separador decimal.
6. No Unity: **Arcane Gambit ▸ FX ▸ Create _AG Variants from FX_ESCOLHAS**. As variantes vão para `Assets/_Game/FX/ETFX_AG`, com um relatório por variante no Console. Rodar de novo regera as variantes sem quebrar referências.
7. As variantes são ligadas sozinhas aos players (`Assets/_Game/FX/Players`): cada slot de partícula desligado chamado "… assign Dust_AG" recebe a variante com esse nome, em modo pool, e é ligado. O Console diz quantos slots foram ligados e quais variantes ainda faltam. Slots que você já preencheu à mão não são tocados. Para repetir: **Arcane Gambit ▸ FX ▸ Link _AG Variants to Players**. `RuneCircle_AG` é opcional: o círculo de runas já é desenhado pelo jogo.

**Escala** multiplica o tamanho do prefab original. Um impacto deve caber numa casa (cerca de 0,25); clima deve cobrir o tabuleiro sem passar da HUD. Os valores abaixo são um ponto de partida, a acertar no `FX_Sandbox`.
**Alfa** multiplica a transparência da paleta (por exemplo, "névoa → pergaminho 40%").

## Variantes

| Variante | Eventos | Tipo procurado | Candidatos | Escolhido | Paleta | Camada | Escala | Alfa |
|---|---|---|---|---|---|---|---|---|
| Dust_AG | Jogar carta (pouso), Mover, Salto da Cavalaria, Recrutar (poof), Paliçada · Estacas · Fosso | poeira, poof, terra | `Prefabs/Environment/Dust/DustDirtyPoofSoft`<br>`Prefabs/Environment/Dust/DustDirtyPoof`<br>`Prefabs/Combat/Explosions (Misc)/HitDustExplosion`<br>`Prefabs/Environment/Smoke/White/SmokeBurstWhiteSoft` |  | Smoke | Tabuleiro | 0,25 | 1 |
| Impact_AG | Captura por movimento | impacto | `Prefabs/Combat/Brawling/Soft/Radial/SoftRadialPunchMedium`<br>`Prefabs/Combat/Brawling/Toon/Radial/ToonRadialPunchMedium`<br>`Prefabs 2D/Explosions/StarBurst2D`<br>`Prefabs/Combat/Explosions (Misc)/HitDustExplosion` |  | Smoke | Tabuleiro | 0,25 | 1 |
| InkSplatter_AG | Raio (impacto), Lamaçal | respingos | `Prefabs 2D/Blood/Red/BloodSplatCritical2D`<br>`Prefabs 2D/Blood/Red/BloodExplosion2D`<br>`Prefabs 2D/Blood/Red/BloodSplatDirectional2D`<br>`Prefabs/Combat/Blood/Red/BloodSplatWide` |  | InkBlot | Tabuleiro | 0,25 | 1 |
| Sparks_AG | Captura bloqueada, Bola de Fogo · bloqueio (3b) | faíscas | `Prefabs 2D/Explosions/SparkExplosion2D`<br>`Prefabs 2D/Explosions/MetalHit2D`<br>`Prefabs/Environment/Sparks/SparkExplosionYellow`<br>`Prefabs/Combat/Explosions (Misc)/MetalExplosion` |  | Lightning | Tabuleiro | 0,25 | 1 |
| CollectGlow_AG | Equipar | brilho de coleta | `Prefabs/Interactive/Loot/ItemSparkleBurst/ItemSparkleBurstWhite`<br>`Prefabs/Interactive/Loot/ItemSparkleSoft/ItemSparkleSoftWhite`<br>`Prefabs/Interactive/Stars/StarPoof`<br>`Prefabs/Interactive/Sparkle/SparkleSolo/SparkleSoloWhite` |  | Snow | Tabuleiro | 0,3 | 1 |
| Glint_AG | Ganhar Moral, Presságio do Vidente | brilho pequeno, brilho mágico | `Prefabs/Interactive/Sparkle/TinySparkle`<br>`Prefabs/Interactive/Sparkle/SparkleSolo/SparkleSoloYellow`<br>`Prefabs/Interactive/Loot/ItemSparkle/ItemSparkleYellow` |  | Arcane | Interface | 0,3 | 1 |
| RuneCircle_AG | Arcano conjurado (atrás da carta), Bola de Fogo · conjuração (sob a unidade) | círculo mágico | `Prefabs/Combat/Magic/Circle/MagicCircleYellow`<br>`Prefabs/Combat/Magic/Circle Simple/MagicCircleSimpleYellow`<br>`Prefabs/Interactive/Zone/MagicZone/MagicZoneYellow`<br>`Prefabs/Interactive/Zone/SpinZone/SpinZoneOrange` |  | Arcane | Tabuleiro | 0,3 | 1 |
| Embers_AG | Arcano conjurado, Bola de Fogo · conjuração, Noite Sem Lua (tochas) | brasas | `Prefabs/Environment/Fireflies/FireFliesRed`<br>`Prefabs/Combat/Magic/Charge/MagicChargeYellow`<br>`Prefabs/Combat/Magic/Aura Soft/AuraSoftYellow`<br>`Prefabs/Environment/Fire/Candle/CandleLight2/CandleLight2` |  | Fire | Tabuleiro | 0,3 | 1 |
| InkCloud_AG | Tempestade (nuvens e casa marcada), Cortina de Fumaça, Bola de Fogo (fumaça), Derrota | nuvem, fumaça | `Prefabs/Environment/Smoke/Dark/SmokeDarkSoft`<br>`Prefabs/Environment/Smoke/Dark/SmokeBurstDarkSoft`<br>`Prefabs/Environment/Fog/Black/FogCalmBlack`<br>`Prefabs/Environment/Smoke/White/SmokeWhiteSoft` |  | Smoke | Tabuleiro | 0,5 | 0,85 |
| Rain_AG | Tempestade de Relâmpagos (chegada e ambiente) | chuva | `Prefabs 2D/Weather/Rain2D`<br>`Prefabs/Environment/Weather/Rain/RainLight`<br>`Prefabs/Environment/Weather/Rain/RainSimple`<br>`Prefabs/Environment/Weather/Rain/RainMedium` |  | Rain | Tabuleiro | 0,5 | 1 |
| LightningBolt_AG | Tempestade · raio, Tempestade · trovão ao fundo | raio | `Prefabs/Environment/Lightning/Sharp Strike/LightningStrikeSharpTallBlue`<br>`Prefabs/Environment/Lightning/Soft Strike/LightningStrikeTallBlue`<br>`Prefabs/Environment/Lightning/Soft Strike/LightningStrikeBlue`<br>`Prefabs/Environment/Lightning/Soft Strike/LightningBlastBlue` |  | Lightning | Tabuleiro | 0,35 | 1 |
| FireExplosion_AG | Bola de Fogo · captura (3a), Raio (explosão pequena) | explosão de fogo | `Prefabs/Combat/Explosions/FireballSoftExplosion/ExplosionFireballSoftFire`<br>`Prefabs/Combat/Explosions/FireballRoundExplosion/ExplosionFireballFire`<br>`Prefabs/Combat/Explosions/SmallExplosion/SmallExplosionFire`<br>`Prefabs/Combat/Explosions/NovaSmallExplosion/ExplosionNovaSmallFire` |  | Fire | Tabuleiro | 0,25 | 1 |
| Fireball_AG | Bola de Fogo · disparo | projétil de fogo | `Prefabs/Combat/Missiles/FireballSoft/FireballSoftMissileFire`<br>`Prefabs/Combat/Missiles/Fireball/FireballMissileFire`<br>`Prefabs/Combat/Missiles/FireballSharp/FireballSharpMissileFire`<br>`Prefabs/Combat/Missiles/MagicSoft/MagicMissileSoftFire` |  | Fire | Tabuleiro | 0,25 | 1 |
| Bolts_AG | Salva de Virotes | projéteis pequenos | `Prefabs/Combat/Missiles/Sharp/SharpMissileBlue`<br>`Prefabs/Combat/Missiles/Bullet/BulletSmallBlue`<br>`Prefabs/Combat/Missiles/Sharp/SharpMissileFire` |  | InkBlot | Tabuleiro | 0,2 | 1 |
| InkTrail_AG | Marcha Forçada · Finta · Avanço | rastro | `Prefabs/Environment/Smoke/White/SmokeWhiteSoftTrail`<br>`Prefabs/Environment/Smoke/Dark/SmokeDarkSoftTrail`<br>`Prefabs/Interactive/Stars/StarTrail` |  | Smoke | Tabuleiro | 0,25 | 1 |
| MagicTrail_AG | Eco do Oráculo | rastro mágico | `Prefabs/Interactive/Stars/StarTrail`<br>`Prefabs/Interactive/Stars/StarStream`<br>`Prefabs/Combat/Missiles/MagicSoft/MagicMissileSoftFire` |  | Arcane | Interface | 0,3 | 1 |
| Snow_AG | Nevasca | neve | `Prefabs/Environment/Weather/Snow/SnowLight`<br>`Prefabs/Environment/Weather/Snow/SnowMedium`<br>`Prefabs/Environment/Weather/Snow/SnowSpinny` |  | Snow | Tabuleiro | 0,5 | 1 |
| Wind_AG | Ventania | vento, folhas, poeira | `Prefabs/Environment/Weather/Wind & Leaves/Windlines`<br>`Prefabs/Environment/Weather/Wind & Leaves/BlowingLeaves`<br>`Prefabs/Environment/Weather/Wind & Leaves/WindlinesStormy`<br>`Prefabs/Environment/Dust/DustMotesLively` |  | Smoke | Tabuleiro | 0,5 | 1 |
| Fog_AG | Neblina | névoa rasteira | `Prefabs/Environment/Fog/White/FogFlatWhite`<br>`Prefabs/Environment/Fog/White/FogCalmWhite`<br>`Prefabs/Environment/Fog/Grey/FogFlatGrey`<br>`Prefabs/Environment/Fog/White/FogLivelyWhite` |  | Snow | Tabuleiro | 0,5 | 0,45 |
| LightRays_AG | Prece ao Amanhecer | raios de luz | `Prefabs/Interactive/Loot/TreasureVerticalGlowRays`<br>`Prefabs/Interactive/Loot/TreasureChestGlowRays`<br>`Prefabs/Interactive/Level Up/Nova/LevelupNovaWhite`<br>`Prefabs/Combat/Magic/PillarBlast/MagicPillarBlastYellow` |  | Snow | Tabuleiro | 0,4 | 1 |
| ImpactRing_AG | Selo da Vigília | anel de impacto | `Prefabs/Combat/Nova/Standard/NovaFire`<br>`Prefabs/Combat/Magic/Nova/MagicNovaYellow`<br>`Prefabs/Interactive/Level Up/Nova/LevelupNovaRed`<br>`Prefabs/Combat/Brawling/RoundHit/RoundHitRed` |  | Arcane | Tabuleiro | 0,3 | 1 |
| Petals_AG | Vitória | brilho e pétalas | `Prefabs/Environment/Confetti/Blast/ConfettiBlastRed`<br>`Prefabs/Environment/Confetti/Shower/ConfettiShowerRed`<br>`Prefabs/Interactive/Feathers/FeatherShower`<br>`Prefabs/Interactive/Feathers/FeatherExplosion` |  | Arcane | Interface | 0,5 | 1 |

## Paletas (`Assets/_Game/FX/Palettes`)

| Paleta | Rampa (FX_SPEC 4.2) | Brilha? |
|---|---|---|
| Fire | Fogo / explosão: luz de vela → rubrica clara → rubrica → tinta, some no fim | Só as camadas que eram aditivas (o núcleo) |
| Lightning | Raio: contorno `#1E140A`, núcleo `#F3E8D2`, halo `#C0573F` a 45% | Só o núcleo |
| Smoke | Fumaça / nuvem / névoa: tinta diluída a 60% → tinta, some | Não |
| Rain | Chuva: tinta a 35%, constante | Não |
| Snow | Neve: pergaminho a 90% → some (também névoa e luz de Prece) | Não |
| Arcane | Arcano / runas: rubrica → rubrica clara → some | Não |
| InkBlot | Mancha de captura: tinta escura a 85%, constante | Não |

A ferramenta decide o papel de cada camada de partícula pelo original: camadas chamadas *Glow*, *Halo* ou *Flare* viram **halo**; camadas que eram aditivas viram **núcleo**; o resto é **corpo**. O relatório no Console mostra o papel de cada uma.
