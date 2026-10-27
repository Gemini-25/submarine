namespace DeepLine.Instruments;

/// <summary>
/// 바퀴식(pinwheel) 탁상 계산기 (SPEC 7절 '선내 계산 도구'). 1930년대 오드너식 기계를 본뜬다.
/// 설정 레버로 수를 놓고 손잡이를 앞으로 한 바퀴 돌리면 설정값 × 10^캐리지가 결과 레지스터에 더해지고, 뒤로 돌리면 빠진다.
/// 회전 수 레지스터는 같은 자리에 ±1을 센다. 레지스터는 기계처럼 자릿수에서 넘친다(결과 13자리, 회전 수 8자리).
/// 빼서 결과가 0 아래로 내려가면 맨 윗자리의 받아내림이 넘쳐 벨이 울리고, 결과 창에는 9로 가득 찬 보수가 남는다.
/// 화면 표시 전용 상태다. 재생과 저장에 들어가지 않는다.
/// </summary>
public sealed class DeskCalculator
{
    public const int SettingDigits = 8;
    public const int ResultDigits = 13;
    public const int CounterDigits = 8;
    /// <summary>캐리지가 갈 수 있는 가장 높은 자리. 설정의 맨 윗자리가 결과의 맨 윗자리에 닿는 곳.</summary>
    public const int MaxCarriage = ResultDigits - SettingDigits;

    public static readonly long ResultModulus = Pow10(ResultDigits);
    public static readonly long CounterModulus = Pow10(CounterDigits);

    readonly int[] _levers = new int[SettingDigits];
    long _result;
    long _counter;

    /// <summary>벨이 울릴 때. Godot 층이 종소리를 낸다.</summary>
    public event Action? Bell;

    /// <summary>설정의 일의 자리가 결과의 몇째 자리 위에 있는지(0이면 일의 자리끼리 맞는다).</summary>
    public int Carriage { get; private set; }

    /// <summary>지금까지 울린 벨 수. 지울 때도 그대로 둔다.</summary>
    public int BellCount { get; private set; }

    /// <summary>설정 레버가 놓인 수.</summary>
    public long Setting
    {
        get
        {
            long v = 0;
            for (int i = SettingDigits - 1; i >= 0; i--) v = v * 10 + _levers[i];
            return v;
        }
    }

    /// <summary>결과 창의 수(0 이상, 13자리 안). 벨이 울린 뒤에는 보수가 보인다.</summary>
    public long Result => _result;

    /// <summary>회전 수 창의 크기. 뒤로 돈 횟수가 더 많으면 CounterNegative가 켜진다(기계의 빨간 숫자).</summary>
    public long Counter => Math.Abs(_counter);

    public bool CounterNegative => _counter < 0;

    /// <summary>place 자리(0이 일의 자리) 설정 레버의 숫자.</summary>
    public int Lever(int place) => _levers[CheckPlace(place, SettingDigits)];

    public void SetLever(int place, int digit)
    {
        if (digit is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(digit));
        _levers[CheckPlace(place, SettingDigits)] = digit;
    }

    /// <summary>레버를 멈춤 칸 단위로 옮긴다. 0과 9에서 멈춘다(Dial 손동작).</summary>
    public void StepLever(int place, int steps) => SetLever(place, Math.Clamp(Lever(place) + steps, 0, 9));

    /// <summary>설정 레버를 한꺼번에 놓는다.</summary>
    public void SetNumber(long n)
    {
        if (n < 0 || n >= Pow10(SettingDigits)) throw new ArgumentOutOfRangeException(nameof(n));
        for (int i = 0; i < SettingDigits; i++, n /= 10) _levers[i] = (int)(n % 10);
    }

    public int ResultDigit(int place) => (int)(_result / Pow10(CheckPlace(place, ResultDigits)) % 10);

    public int CounterDigit(int place) => (int)(Counter / Pow10(CheckPlace(place, CounterDigits)) % 10);

    /// <summary>캐리지를 한 자리 위로(왼쪽으로) 옮긴다. 끝이면 움직이지 않고 false.</summary>
    public bool ShiftUp() => ShiftTo(Carriage + 1);

    public bool ShiftDown() => ShiftTo(Carriage - 1);

    public bool ShiftTo(int place)
    {
        if (place < 0 || place > MaxCarriage) return false;
        Carriage = place;
        return true;
    }

    /// <summary>손잡이를 한 바퀴 돌린다. forward면 더하고 아니면 뺀다. 벨이 울리면 true.</summary>
    public bool Turn(bool forward)
    {
        long step = Pow10(Carriage);
        long amount = Setting * step;
        bool bell = false;
        if (forward)
        {
            _result = (_result + amount) % ResultModulus;
            _counter = (_counter + step) % CounterModulus;
        }
        else
        {
            _result -= amount;
            if (_result < 0)
            {
                // 맨 윗자리 받아내림이 넘친다. 결과는 보수로 돌고 벨이 울린다.
                _result += ResultModulus;
                bell = true;
            }
            _counter = (_counter - step) % CounterModulus;
        }
        if (bell)
        {
            BellCount++;
            Bell?.Invoke();
        }
        return bell;
    }

    public bool Forward() => Turn(true);

    public bool Back() => Turn(false);

    public void ClearResult() => _result = 0;

    public void ClearCounter() => _counter = 0;

    public void ClearSetting() => Array.Clear(_levers);

    /// <summary>레지스터 셋을 지우고 캐리지를 일의 자리로 돌린다.</summary>
    public void ClearAll()
    {
        ClearResult();
        ClearCounter();
        ClearSetting();
        Carriage = 0;
    }

    public static long Pow10(int n)
    {
        long v = 1;
        for (int i = 0; i < n; i++) v *= 10;
        return v;
    }

    public static int Digits(long n)
    {
        int d = 1;
        while (n >= 10) { n /= 10; d++; }
        return d;
    }

    static int CheckPlace(int place, int digits) =>
        place >= 0 && place < digits ? place : throw new ArgumentOutOfRangeException(nameof(place));
}

/// <summary>계산기 조작 한 걸음. WP22 시험 묶음이 이 목록을 손동작으로 옮긴다.</summary>
public enum CalcStep { ClearAll, ClearCounter, ClearSetting, Set, Shift, Forward, Back }

/// <summary>Value는 Set의 수, Shift의 캐리지 자리. 나머지는 쓰지 않는다.</summary>
public readonly record struct CalcAction(CalcStep Step, long Value = 0);

/// <summary>
/// 교범 09쪽의 곱셈과 나눗셈 순서를 기계 위에서 그대로 밟는 조작자. 계산은 기계가 하고, 여기서는 손잡이와 캐리지만 움직인다.
/// 밟은 걸음을 돌려준다(손잡이를 돈 횟수만큼 소음이 난다).
/// </summary>
public static class CalculatorDrill
{
    /// <summary>
    /// 곱셈 a × b. a를 설정에 놓고, b의 일의 자리부터 캐리지를 한 자리씩 올리며 그 숫자만큼 앞으로 돌린다.
    /// 끝나면 결과 창에 곱, 회전 수 창에 b가 남는다.
    /// </summary>
    public static List<CalcAction> Multiply(DeskCalculator c, long a, long b)
    {
        if (a < 0 || a >= DeskCalculator.Pow10(DeskCalculator.SettingDigits)) throw new ArgumentOutOfRangeException(nameof(a));
        if (b < 0 || b >= DeskCalculator.Pow10(DeskCalculator.MaxCarriage + 1)) throw new ArgumentOutOfRangeException(nameof(b));
        var log = new List<CalcAction>();
        Do(c, log, new(CalcStep.ClearAll));
        Do(c, log, new(CalcStep.Set, a));
        for (int place = 0; b > 0; place++, b /= 10)
        {
            Do(c, log, new(CalcStep.Shift, place));
            for (int n = 0; n < b % 10; n++) Do(c, log, new(CalcStep.Forward));
        }
        return log;
    }

    /// <summary>
    /// 나눗셈 dividend ÷ divisor, 소수 decimals 자리까지. 피제수를 결과 창에 올린 뒤 제수의 맨 윗자리를 피제수의 맨 윗자리에 맞추고,
    /// 벨이 울릴 때까지 뒤로 돌린다. 벨이 울리면 앞으로 한 번 돌려 되돌리고 캐리지를 한 자리 내린다. 일의 자리까지 되풀이한다.
    /// 끝나면 회전 수 창에 몫 × 10^decimals가 빨간 숫자로, 결과 창에 나머지 × 10^decimals가 남는다.
    /// </summary>
    public static List<CalcAction> Divide(DeskCalculator c, long dividend, long divisor, int decimals = 0)
    {
        if (divisor <= 0 || divisor >= DeskCalculator.Pow10(DeskCalculator.SettingDigits)) throw new ArgumentOutOfRangeException(nameof(divisor));
        if (dividend < 0 || dividend >= DeskCalculator.Pow10(DeskCalculator.SettingDigits)) throw new ArgumentOutOfRangeException(nameof(dividend));
        if (decimals < 0 || decimals > DeskCalculator.MaxCarriage) throw new ArgumentOutOfRangeException(nameof(decimals));
        long scaled = dividend * DeskCalculator.Pow10(decimals);
        if (scaled / divisor >= DeskCalculator.CounterModulus) throw new ArgumentOutOfRangeException(nameof(dividend), "몫이 회전 수 창을 넘는다");

        var log = new List<CalcAction>();
        Do(c, log, new(CalcStep.ClearAll));
        Do(c, log, new(CalcStep.Set, dividend));
        Do(c, log, new(CalcStep.Shift, decimals));
        Do(c, log, new(CalcStep.Forward));
        Do(c, log, new(CalcStep.ClearCounter));
        Do(c, log, new(CalcStep.Set, divisor));

        int top = Math.Clamp(DeskCalculator.Digits(scaled) - DeskCalculator.Digits(divisor), 0, DeskCalculator.MaxCarriage);
        for (int place = top; place >= 0; place--)
        {
            Do(c, log, new(CalcStep.Shift, place));
            while (!Do(c, log, new(CalcStep.Back))) { }
            Do(c, log, new(CalcStep.Forward));
        }
        return log;
    }

    /// <summary>걸음 하나를 기계에 적용한다. 벨이 울리면 true.</summary>
    public static bool Apply(DeskCalculator c, CalcAction a)
    {
        switch (a.Step)
        {
            case CalcStep.ClearAll: c.ClearAll(); return false;
            case CalcStep.ClearCounter: c.ClearCounter(); return false;
            case CalcStep.ClearSetting: c.ClearSetting(); return false;
            case CalcStep.Set: c.SetNumber(a.Value); return false;
            case CalcStep.Shift:
                if (!c.ShiftTo((int)a.Value)) throw new ArgumentOutOfRangeException(nameof(a));
                return false;
            case CalcStep.Forward: return c.Forward();
            case CalcStep.Back: return c.Back();
            default: throw new ArgumentOutOfRangeException(nameof(a));
        }
    }

    static bool Do(DeskCalculator c, List<CalcAction> log, CalcAction a)
    {
        log.Add(a);
        return Apply(c, a);
    }
}
