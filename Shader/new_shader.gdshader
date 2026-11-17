shader_type canvas_item; // Изменено с spatial на canvas_item

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform int pixel_size = 4; // Вынесено в uniform для удобной настройки в инспекторе

void fragment() {
    // SCREEN_UV — это нормализованные координаты (0.0 - 1.0) всего экрана
    vec2 screen_size = 1.0 / SCREEN_PIXEL_SIZE; // Получаем размер экрана в пикселях
    
    // Переводим UV в пиксельные координаты
    vec2 frag_coord = SCREEN_UV * screen_size;
    
    // Вычисляем координаты с учётом размера пикселя
    float x = floor(frag_coord.x / float(pixel_size)) * float(pixel_size) + floor(float(pixel_size) / 2.0);
    float y = floor(frag_coord.y / float(pixel_size)) * float(pixel_size) + floor(float(pixel_size) / 2.0);
    
    // Нормализуем обратно в UV для выборки из текстуры
    vec2 new_uv = vec2(x, y) / screen_size;
    
    // Читаем цвет из текстуры экрана
    COLOR = texture(screen_texture, new_uv);
}