# Ponteirinho

Uma mãozinha com uma vareta de professor que aponta para onde está o mouse. Feito para apresentações ao vivo no Windows.

## Como usar

- Abra `Ponteirinho-app\Ponteirinho.exe`.
- **Ctrl+Alt+H** mostra e esconde a mãozinha.
- O ícone da mãozinha fica perto do relógio (talvez escondido na setinha `^`):
  - **clique esquerdo:** liga e desliga;
  - **clique direito:** abre as opções.
- Ao clicar, a mão dá um "cutucão", toca um barulhinho e aparece uma onda na ponta do dedo.

### Opções (clique direito no ícone)

| Menu | Escolhas |
|---|---|
| Tipo de mão | **Mãos 3D (TinyHand):** 36 mãos prontas (Lavanda, Luva de cursor, Robô, Pelúcia, Pixel, Neon...), cada uma com a sua vareta. **Com cor à sua escolha:** Massinha 3D, Fofinha, Luva de desenho, Clássica. E o Laser (só um ponto) |
| Cor | só para as mãos "com cor à sua escolha" e o Laser: Lavanda, Rosa, Vermelho, Laranja, Amarelo, Verde, Azul, Branco, Preto, 3 tons de pele |
| Tamanho | Pequena, Média, Grande, Enorme |
| Vareta | de onde vem (direita, centro, esquerda) e cor (preta ou madeira; a cor só vale para as mãos pintáveis, as 3D usam a vareta do próprio desenho) |
| Som ao clicar | Desligado, Pop suave, Toque de madeira, Bolha + volume baixo/médio/alto |
| Esconder o cursor do mouse | ligado por padrão: enquanto a mão aparece, a ponta do dedo vira o cursor |

### Se o cursor do mouse sumir

Com a mão desligada (Ctrl+Alt+H) ou o app fechado, o cursor volta sozinho. Um processo "vigia" restaura o cursor até se o app travar ou for fechado à força. Se mesmo assim ele não voltar, use **só o teclado**:

1. aperte a **tecla Windows**;
2. digite **Restaurar cursor**;
3. aperte **Enter**.

Abrir o Ponteirinho de novo, sair da conta ou reiniciar o computador também resolve.

**No Meet, Teams ou Zoom:** compartilhe a **tela inteira**. Se compartilhar só uma janela, a mão não aparece para os outros. O barulhinho só chega a eles se você marcar "compartilhar áudio do computador".

As preferências ficam salvas em `%APPDATA%\Ponteirinho\settings.json`.

## Pastas

| Pasta | O que tem |
|---|---|
| `Ponteirinho-app\` | o app pronto para usar (precisa do .NET 8, que já está instalado) |
| `Ponteirinho\` | o código-fonte (C# + WPF) |
| `arte\tinyhand-originais\` | as 36 imagens originais do TinyHand, com a licença |
| `Ponteirinho\Assets\tinyhand\` | as mesmas mãos já recortadas e reduzidas para o app (`tools\prepare-tinyhand.ps1` refaz isso) |
| `arte\mao-3d.figcraft` | a mão Massinha 3D, para editar no FigCraft (feita com sombras internas e desfoques) |
| `arte\maos.vectorcraft` | as mãos Fofinha, Luva e Clássica (uma prancheta por tipo), para editar no VectorCraft |

## Créditos

As 36 mãos 3D vêm do app TinyHand para Mac (github.com/nilbuild/tinyhand), de Kamran Ahmed, sob **licença MIT**. Essa licença permite usar e modificar as imagens, desde que o aviso dela vá junto. Por isso o arquivo `LICENSE-TinyHand.txt` fica ao lado do `Ponteirinho.exe`, e o menu tem o item "Créditos".

## Como as cores funcionam (mãos pintáveis)

Cada mão pintável é desenhada **em tons de cinza**, e o app pinta o cinza com a cor escolhida:

- **cinza médio** (`#808080`) vira a cor exata;
- **mais escuro** vira sombra ou contorno;
- **mais claro** vira brilho (branco continua branco).

O que for desenhado **colorido** fica como está, como o punho azul da Clássica.

## Trocar ou criar uma mão

1. Edite `arte\maos.vectorcraft` sem mudar o tamanho das pranchetas (200 × 260).
2. Exporte a prancheta em PNG com escala 2 para `Ponteirinho\Assets\hand-<nome>.png`.
3. Em `Ponteirinho\HandStyles.cs`, na lista `HandStyle.All`, informe dois pontos medidos na prancheta:
   - a **ponta do dedo** (`TipX`, `TipY`);
   - o **ponto do punho** onde a vareta encaixa (`WristX`, `WristY`).
4. Para mudar o ícone do app, rode `Ponteirinho\tools\make-icon.ps1` (aceita `-Png` e `-Color`).
5. Compile de novo (veja abaixo).

## Compilar

Dentro da pasta `Ponteirinho\`:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o ..\Ponteirinho-app
```

Feche o Ponteirinho antes, senão o `.exe` fica bloqueado.
