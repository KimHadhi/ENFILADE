# 코드 도구 (Tools/CodeTools)

Unity를 켜지 않고 게임 코드 전체를 읽어서 검사하고 정리하는 도구다. C# 컴파일러의 라이브러리(Roslyn)로 스크립트를
**메모리에서만** 읽는다. Unity가 마지막으로 컴파일할 때 쓴 설정(`Library/Bee/artifacts/*.dag/*.rsp`: 기호, 참조, 파일 목록)을
그대로 쓰므로 Unity의 컴파일과 같은 답이 나온다. 0.9.1~0.9.3의 정리(죽은 코드 삭제, 사본 합치기, 큰 파일 나누기)를 이 도구로 했다.

```powershell
& ./Tools/Check-Code.ps1            # 컴파일 검사 (에디터 구성 + 플레이어 빌드 구성). 몇 초
& ./Tools/Check-Code.ps1 dead       # 아무도 쓰지 않는 코드 → Logs/CodeTools/dead.txt
& ./Tools/Check-Code.ps1 dupes      # 낱말까지 같은 메서드 사본 → Logs/CodeTools/dupes.txt
& ./Tools/Check-Code.ps1 stats      # 파일·메서드 크기, 흩어진 호출 수
```

.NET SDK가 깔려 있어야 한다(받는 패키지는 없다: Roslyn은 SDK 안의 것을 쓴다). 프로젝트를 Unity로 한 번은 열어 컴파일한 적이 있어야 한다.

## 명령

검사 명령은 프로젝트에 아무것도 쓰지 않는다. 고치는 명령은 `--dry`를 붙이면 무엇을 할지만 보여 준다. **고친 뒤에는 꼭 `check`를 돌린다.**

| 명령 | 하는 일 |
| --- | --- |
| `check [--player]` | 컴파일 오류를 찾는다. `--player`는 `UNITY_EDITOR` 없이, 에디터 어셈블리 없이 한 번 더 (빌드에서만 나는 오류) |
| `errors` | 오류를 파일별로 묶어 센다 |
| `warn` | 프로젝트 코드의 컴파일 경고 → `warnings.txt` |
| `stats` | 크기와 흩어진 패턴의 수 → `stats.txt` |
| `dead` | 죽은 코드와 "쓰기만 하고 읽지 않는" 멤버 → `dead.txt`, `dead.json` |
| `dupes` / `clones` | 같은 메서드 사본 / 이름만 다른 닮은꼴 → `dupes.txt`, `clones.txt` |
| `outline <파일>` | 한 파일의 멤버와 줄 수 (큰 파일을 어떻게 나눌지 볼 때) |
| `kill [--skip serialized,structField] [--except @목록] [--dry]` | 죽은 선언을 주석째 지운다. 다 죽은 파일은 `.meta`와 함께 지운다 |
| `forwarders [--dry]` | 인자를 그대로 넘기기만 하는 static 한 줄 메서드의 호출부가 진짜 대상을 바로 부르게 한다 |
| `dedupe [--homes 파일] [--map 파일] [--dry]` | 사본들의 호출부를 "집" 하나로 돌린다. 그 뒤 `kill`이 사본을 지운다 |
| `lift --base 이름 --file 경로 --types A,B --primary A --summary "…"` | 여러 클래스가 낱말까지 같게 가진 멤버를 새 바탕 클래스로 올린다 |
| `split <파일> [--names "A;B"] [--parts "이름:시작-끝,…"] [--titles "이름=설명;…"]` | 큰 클래스를 partial 파일로 나눈다 (`// ---- 주제 ----` 줄에서, 또는 줄 범위로) |
| `usings [--dry]` | 아무것도 쓰지 않는 `using` 줄을 지운다 (에디터·플레이어 구성 둘 다에서 안 쓸 때만) |
| `remove Type.Method/2,…` | 이름 붙인 메서드를 지운다 |

## 죽은 코드를 어떻게 아는가

출발점에서 닿지 않는 선언이 죽은 것이다. 출발점은: Unity가 부르는 메서드(Awake, Update …), 프리팹·씬·에셋에 붙은 스크립트(GUID),
`[RuntimeInitializeOnLoadMethod]`·`[MenuItem]` 같은 속성이 붙은 것, 인터페이스 구현과 override, 에셋이 이름으로 부르는
메서드(UnityEvent, 애니메이션 이벤트), `Invoke("이름")`·`FindProperty("이름")`처럼 글자로 부르는 것, 에셋 팩 코드가 쓰는 것.
에디터 구성과 플레이어 구성 둘 다에서 죽어 있어야 죽은 것으로 본다.

도구가 볼 수 없어서 조심할 것:

- **직렬화 필드**(public, `[SerializeField]`)는 코드가 읽지 않아도 뜻이 있을 수 있다(셰이더를 빌드에 넣으려고 잡아 둔 참조 등). 그래서 `kill`은 보통 `--skip serialized,structField`로 돌리고 목록을 눈으로 본다.
- 표가 이름으로 채우는 필드(`GameTables.Bind`)는 코드가 **읽으면** 살아 있다. 읽지 않는데 표에만 있으면 "듣지 않는 숫자"다: 연결하거나 표에서도 지운다.
- 이름이 다른 글자와 우연히 같은 것은 `literal` 표시가 붙는다. 대부분 지워도 되지만 한 번 본다.

## 사본을 합칠 때

`dedupe`는 **이름도 같고 속도 같은**(이름 하나하나가 같은 것을 가리키는지까지) static 메서드만 저절로 합친다. 묶음에서
private이 아닌 것이 하나면 그것이 집이다. 집을 정해 주려면 `--homes`(한 줄에 `Type.Method/인자수`), 모양은 달라도 읽어 보니 같은
계산이면 `--map`(`From.Method/1 -> To.Method/1`)에 적는다. 같은 글자여도 뜻이 다른 것(이름이 다른 것)은 합치지 않는다.

## 큰 파일을 나눌 때

`split`은 멤버를 다른 파일로 옮기기만 하고 한 글자도 고치지 않는다. 속성이 붙은 필드와 시작값이 있는 static 필드는 본 파일에
남긴다(인스펙터의 순서와 static이 채워지는 순서가 그대로이게). 새 파일 이름은 `클래스.주제.cs`이고 본 파일은 `클래스.cs` 그대로다.
