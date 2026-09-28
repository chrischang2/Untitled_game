extends CanvasLayer

signal closed

const ITEMS := [
	{"name": "Pickaxe Upgrade", "cost": 10, "action": "punch_damage", "amount": 1},
	{"name": "Speed Boots", "cost": 15, "action": "speed", "amount": 1.0},
	{"name": "Scrap Magnet", "cost": 20, "action": "pickup_radius", "amount": 0.75},
]

@onready var _root: Control = $Root
@onready var _currency_label: Label = $Root/Panel/VBoxContainer/CurrencyLabel
@onready var _button_container: VBoxContainer = $Root/Panel/VBoxContainer

var _player: Node = null

func _ready() -> void:
	visible = true
	_root.visible = false
	GameState.currency_changed.connect(_on_currency_changed)
	_build_buttons()


func open(player: Node) -> void:
	_player = player
	_root.visible = true
	_update_currency_label()


func close() -> void:
	_root.visible = false
	closed.emit()


func is_open() -> bool:
	return _root.visible


func _build_buttons() -> void:
	for item in ITEMS:
		var button := Button.new()
		button.text = "%s - %d chunks" % [item["name"], item["cost"]]
		button.pressed.connect(_on_item_pressed.bind(item))
		_button_container.add_child(button)

	var close_button := Button.new()
	close_button.text = "Close"
	close_button.pressed.connect(close)
	_button_container.add_child(close_button)


func _on_item_pressed(item: Dictionary) -> void:
	if _player == null or not GameState.spend_currency(item["cost"]):
		return
	match item["action"]:
		"punch_damage":
			_player.upgrade_punch_damage(item["amount"])
		"speed":
			_player.upgrade_speed(item["amount"])
		"pickup_radius":
			_player.upgrade_pickup_radius(item["amount"])
	_update_currency_label()


func _on_currency_changed(_value: int) -> void:
	_update_currency_label()


func _update_currency_label() -> void:
	_currency_label.text = "Currency: %d" % GameState.currency
