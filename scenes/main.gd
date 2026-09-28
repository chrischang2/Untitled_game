extends Node3D

const ROOM_WIDTH := 10.0
const ROOM_HEIGHT := 4.0
const ROOM_DEPTH := 10.0
const WALL_THICKNESS := 0.3

const CHUNK_COLS := 5
const CHUNK_ROWS := 4
const WINDOW_COL := 2
const WINDOW_ROW := 1

const SHOP_WIDTH := 4.0
const SHOP_DEPTH := 3.0

const ROOM2_HEIGHT := 5.0
const ROOM2_DEPTH := 16.0
const WALL2_COLS := 6
const WALL2_ROWS := 5

@onready var player: CharacterBody3D = $Player
@onready var shop_ui: CanvasLayer = $ShopUI

var _hud_currency_label: Label
var _hud_prompt_label: Label
var _player_in_shop_range := false

func _ready() -> void:
	_build_room()
	_build_destroyable_wall()
	_build_window()
	_build_shop()
	_build_light()
	_build_hud()
	_build_shop_interact_area()

	player.position = Vector3(ROOM_WIDTH / 2.0, 1.0, ROOM_DEPTH / 2.0)
	player.get_node("Pivot").rotation.y = PI

	GameState.currency_changed.connect(_on_currency_changed)
	shop_ui.closed.connect(_on_shop_closed)


func _process(_delta: float) -> void:
	if _player_in_shop_range and not shop_ui.is_open() and Input.is_action_just_pressed("interact"):
		shop_ui.open(player)
		_hud_prompt_label.visible = false


func _build_room() -> void:
	var wall_color := Color(0.95, 0.95, 0.93)
	_add_static_box(Vector3(ROOM_WIDTH / 2.0, -0.1, ROOM_DEPTH / 2.0), Vector3(ROOM_WIDTH, 0.2, ROOM_DEPTH), wall_color)
	_add_static_box(Vector3(ROOM_WIDTH / 2.0, ROOM_HEIGHT + 0.1, ROOM_DEPTH / 2.0), Vector3(ROOM_WIDTH, 0.2, ROOM_DEPTH), wall_color)

	# The entrance (south) wall has the shop window; west/east are plain. The north wall is the destroyable one, built separately.
	_build_entrance_wall()
	_add_static_box(Vector3(-WALL_THICKNESS / 2.0, ROOM_HEIGHT / 2.0, ROOM_DEPTH / 2.0), Vector3(WALL_THICKNESS, ROOM_HEIGHT, ROOM_DEPTH), wall_color)
	_add_static_box(Vector3(ROOM_WIDTH + WALL_THICKNESS / 2.0, ROOM_HEIGHT / 2.0, ROOM_DEPTH / 2.0), Vector3(WALL_THICKNESS, ROOM_HEIGHT, ROOM_DEPTH), wall_color)


func _build_entrance_wall() -> void:
	var chunk_w := ROOM_WIDTH / CHUNK_COLS
	var chunk_h := ROOM_HEIGHT / CHUNK_ROWS
	var wall_color := Color(0.95, 0.95, 0.93)
	for col in range(CHUNK_COLS):
		for row in range(CHUNK_ROWS):
			if col == WINDOW_COL and row == WINDOW_ROW:
				continue
			_add_static_box(Vector3((col + 0.5) * chunk_w, (row + 0.5) * chunk_h, -WALL_THICKNESS / 2.0), Vector3(chunk_w, chunk_h, WALL_THICKNESS), wall_color)


func _build_destroyable_wall() -> void:
	_build_destroyable_wall_grid(ROOM_DEPTH + WALL_THICKNESS / 2.0, ROOM_WIDTH, ROOM_HEIGHT, CHUNK_COLS, CHUNK_ROWS, -1, -1, _on_wall_one_cleared)


# Builds a grid of destroyable chunks at the given z depth and calls on_cleared once every chunk (except the skipped cell) is broken.
func _build_destroyable_wall_grid(z: float, width: float, height: float, cols: int, rows: int, skip_col: int, skip_row: int, on_cleared: Callable) -> void:
	var chunk_w := width / cols
	var chunk_h := height / rows
	var remaining := 0
	for col in range(cols):
		for row in range(rows):
			if col == skip_col and row == skip_row:
				continue
			remaining += 1
	var remaining_box: Array = [remaining]
	for col in range(cols):
		for row in range(rows):
			if col == skip_col and row == skip_row:
				continue
			var chunk := StaticBody3D.new()
			chunk.set_script(load("res://scripts/destroyable_chunk.gd"))
			chunk.size = Vector3(chunk_w * 0.95, chunk_h * 0.95, WALL_THICKNESS)
			add_child(chunk)
			chunk.position = Vector3((col + 0.5) * chunk_w, (row + 0.5) * chunk_h, z)
			chunk.destroyed.connect(func() -> void:
				remaining_box[0] -= 1
				if remaining_box[0] <= 0:
					on_cleared.call()
			)


func _on_wall_one_cleared() -> void:
	_build_room_two()


func _build_room_two() -> void:
	var near_z := ROOM_DEPTH
	var far_z := near_z + ROOM2_DEPTH
	var center_z := (near_z + far_z) / 2.0
	var color := Color(0.92, 0.92, 0.9)

	_add_static_box(Vector3(ROOM_WIDTH / 2.0, -0.1, center_z), Vector3(ROOM_WIDTH, 0.2, ROOM2_DEPTH), color)
	_add_static_box(Vector3(ROOM_WIDTH / 2.0, ROOM2_HEIGHT + 0.1, center_z), Vector3(ROOM_WIDTH, 0.2, ROOM2_DEPTH), color)
	_add_static_box(Vector3(-WALL_THICKNESS / 2.0, ROOM2_HEIGHT / 2.0, center_z), Vector3(WALL_THICKNESS, ROOM2_HEIGHT, ROOM2_DEPTH), color)
	_add_static_box(Vector3(ROOM_WIDTH + WALL_THICKNESS / 2.0, ROOM2_HEIGHT / 2.0, center_z), Vector3(WALL_THICKNESS, ROOM2_HEIGHT, ROOM2_DEPTH), color)
	# Header strip bridges room one's lower ceiling up to room two's taller ceiling.
	_add_static_box(Vector3(ROOM_WIDTH / 2.0, ROOM_HEIGHT + (ROOM2_HEIGHT - ROOM_HEIGHT) / 2.0, near_z), Vector3(ROOM_WIDTH, ROOM2_HEIGHT - ROOM_HEIGHT, WALL_THICKNESS), color)

	_build_light(Vector3(ROOM_WIDTH / 2.0, ROOM2_HEIGHT - 0.1, center_z))
	_build_destroyable_wall_grid(far_z + WALL_THICKNESS / 2.0, ROOM_WIDTH, ROOM2_HEIGHT, WALL2_COLS, WALL2_ROWS, -1, -1, func() -> void: pass)


func _window_center() -> Vector3:
	var chunk_w := ROOM_WIDTH / CHUNK_COLS
	var chunk_h := ROOM_HEIGHT / CHUNK_ROWS
	return Vector3((WINDOW_COL + 0.5) * chunk_w, (WINDOW_ROW + 0.5) * chunk_h, -WALL_THICKNESS / 2.0)


func _build_window() -> void:
	var chunk_w := ROOM_WIDTH / CHUNK_COLS
	var chunk_h := ROOM_HEIGHT / CHUNK_ROWS
	var center := _window_center()

	_add_static_box(center, Vector3(chunk_w * 0.95, chunk_h * 0.95, WALL_THICKNESS * 0.4), Color(0.15, 0.15, 0.15))

	var glass := StaticBody3D.new()
	var mesh_instance := MeshInstance3D.new()
	var box_mesh := BoxMesh.new()
	box_mesh.size = Vector3(chunk_w * 0.8, chunk_h * 0.8, 0.05)
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.6, 0.85, 0.9, 0.35)
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	box_mesh.material = material
	mesh_instance.mesh = box_mesh
	glass.add_child(mesh_instance)

	var shape := CollisionShape3D.new()
	var box_shape := BoxShape3D.new()
	box_shape.size = Vector3(chunk_w * 0.8, chunk_h * 0.8, 0.05)
	shape.shape = box_shape
	glass.add_child(shape)

	glass.position = center
	add_child(glass)


func _build_shop() -> void:
	var center := _window_center()
	var shop_near_z := -WALL_THICKNESS
	var shop_far_z := shop_near_z - SHOP_DEPTH
	var shop_center_z := (shop_near_z + shop_far_z) / 2.0
	var left_x := center.x - SHOP_WIDTH / 2.0
	var right_x := center.x + SHOP_WIDTH / 2.0
	var shop_color := Color(0.8, 0.78, 0.7)

	_add_static_box(Vector3(center.x, -0.1, shop_center_z), Vector3(SHOP_WIDTH, 0.2, SHOP_DEPTH), shop_color)
	_add_static_box(Vector3(center.x, ROOM_HEIGHT + 0.1, shop_center_z), Vector3(SHOP_WIDTH, 0.2, SHOP_DEPTH), shop_color)
	_add_static_box(Vector3(center.x, ROOM_HEIGHT / 2.0, shop_far_z - WALL_THICKNESS / 2.0), Vector3(SHOP_WIDTH, ROOM_HEIGHT, WALL_THICKNESS), shop_color)
	_add_static_box(Vector3(left_x - WALL_THICKNESS / 2.0, ROOM_HEIGHT / 2.0, shop_center_z), Vector3(WALL_THICKNESS, ROOM_HEIGHT, SHOP_DEPTH), shop_color)
	_add_static_box(Vector3(right_x + WALL_THICKNESS / 2.0, ROOM_HEIGHT / 2.0, shop_center_z), Vector3(WALL_THICKNESS, ROOM_HEIGHT, SHOP_DEPTH), shop_color)

	_add_static_box(Vector3(center.x, 0.5, shop_near_z - 0.5), Vector3(SHOP_WIDTH * 0.7, 1.0, 0.6), Color(0.45, 0.3, 0.2))

	var shopkeeper := MeshInstance3D.new()
	var capsule := CapsuleMesh.new()
	capsule.radius = 0.35
	capsule.height = 1.6
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.5, 0.2, 0.6)
	capsule.material = material
	shopkeeper.mesh = capsule
	shopkeeper.position = Vector3(center.x, 0.9, shop_far_z + 0.8)
	add_child(shopkeeper)


func _build_light(center: Vector3 = Vector3(ROOM_WIDTH / 2.0, ROOM_HEIGHT - 0.1, ROOM_DEPTH / 2.0)) -> void:
	var fixture := MeshInstance3D.new()
	var box_mesh := BoxMesh.new()
	box_mesh.size = Vector3(3.0, 0.1, 0.4)
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(1, 1, 1)
	material.emission_enabled = true
	material.emission = Color(1, 1, 1)
	material.emission_energy_multiplier = 3.0
	box_mesh.material = material
	fixture.mesh = box_mesh
	fixture.position = center
	add_child(fixture)

	var light := OmniLight3D.new()
	light.light_color = Color(1.0, 1.0, 0.98)
	light.light_energy = 2.5
	light.omni_range = 14.0
	light.shadow_enabled = true
	light.position = center - Vector3(0, 0.1, 0)
	add_child(light)


func _build_shop_interact_area() -> void:
	var center := _window_center()
	var area := Area3D.new()
	var shape := CollisionShape3D.new()
	var box_shape := BoxShape3D.new()
	box_shape.size = Vector3(2.5, 2.0, 1.5)
	shape.shape = box_shape
	area.add_child(shape)
	area.position = Vector3(center.x, 1.0, 1.0)
	area.body_entered.connect(_on_shop_area_body_entered)
	area.body_exited.connect(_on_shop_area_body_exited)
	add_child(area)


func _on_shop_area_body_entered(body: Node) -> void:
	if body == player:
		_player_in_shop_range = true
		_hud_prompt_label.visible = true


func _on_shop_area_body_exited(body: Node) -> void:
	if body == player:
		_player_in_shop_range = false
		_hud_prompt_label.visible = false


func _on_shop_closed() -> void:
	if _player_in_shop_range:
		_hud_prompt_label.visible = true


func _build_hud() -> void:
	var hud := CanvasLayer.new()
	add_child(hud)

	_hud_currency_label = Label.new()
	_hud_currency_label.text = "Chunks: 0"
	_hud_currency_label.position = Vector2(16, 16)
	_hud_currency_label.add_theme_font_size_override("font_size", 20)
	hud.add_child(_hud_currency_label)

	_hud_prompt_label = Label.new()
	_hud_prompt_label.text = "Press E to shop"
	_hud_prompt_label.visible = false
	_hud_prompt_label.anchor_left = 0.5
	_hud_prompt_label.anchor_right = 0.5
	_hud_prompt_label.anchor_top = 1.0
	_hud_prompt_label.anchor_bottom = 1.0
	_hud_prompt_label.offset_left = -80.0
	_hud_prompt_label.offset_right = 80.0
	_hud_prompt_label.offset_top = -80.0
	_hud_prompt_label.offset_bottom = -40.0
	_hud_prompt_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hud_prompt_label.add_theme_font_size_override("font_size", 20)
	hud.add_child(_hud_prompt_label)


func _on_currency_changed(new_value: int) -> void:
	_hud_currency_label.text = "Chunks: %d" % new_value


func _add_static_box(pos: Vector3, size: Vector3, color: Color) -> void:
	var body := StaticBody3D.new()
	var mesh_instance := MeshInstance3D.new()
	var box_mesh := BoxMesh.new()
	box_mesh.size = size
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	box_mesh.material = material
	mesh_instance.mesh = box_mesh
	body.add_child(mesh_instance)

	var shape := CollisionShape3D.new()
	var box_shape := BoxShape3D.new()
	box_shape.size = size
	shape.shape = box_shape
	body.add_child(shape)

	body.position = pos
	add_child(body)
