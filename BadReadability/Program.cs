class Program
{
    static void Main(string[] a)
    {
        if (a.Length < 1) return;
        int n;
        if (!int.TryParse(a[0], out n)) return;
        if (n % 2 == 0)
        {
            Console.WriteLine($"Input number {n} is even.");
        }
        else
        {
            Console.WriteLine($"Input number {n} is odd.");
        }
        for (int i = 0; i < n; i++)
        {
            if (i % 2 == 0)
            {
                Console.WriteLine("Index is even.");
            }
            else
            {
                Console.WriteLine("Index is odd.");
            }
        }
        if (a.Length > 1)
        {
            string s = a[1];
            Console.WriteLine($"Second input parameter is {s}.");
            for (int i = s.Length - 1; i >= 0; i--)
            {
                Console.Write(s[i]);
            }
        }
    }
}