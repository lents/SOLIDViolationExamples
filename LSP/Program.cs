//В этом примере класс Square наследуется от класса Rectangle.
//Однако в классе Square мы переопределяем свойства Width и Height таким образом, чтобы они всегда сохраняли равные значения.
//Это противоречит поведению прямоугольника, где изменение одного измерения (ширины или высоты) не должно влиять на другое измерение.

//Это нарушает принцип подстановки Барбары Лисков, поскольку объекты производного класса (Square) не могут быть заменены объектами базового класса (Rectangle) без изменения поведения программы.
//В частности, установка ширины и высоты объекта Square через ссылку на базовый класс
using System;

public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }

    public int Area()
    {
        return Width * Height;
    }
}

public class Square : Rectangle
{
    public override int Width
    {
        get { return base.Width; }
        set
        {
            base.Width = value;
            base.Height = value;
        }
    }

    public override int Height
    {
        get { return base.Height; }
        set
        {
            base.Height = value;
            base.Width = value;
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        Rectangle rectangle = new Square();
        rectangle.Width = 4;
        rectangle.Height = 5;

        Console.WriteLine("Area of rectangle: " + rectangle.Area());
    }
}
