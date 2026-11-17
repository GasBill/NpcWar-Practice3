namespace TowerDefense;

// Компонент атаки, который вызывается общим таймером своей команды.
public interface INpcAttack
{
    void TickAttack(float delta);
}
