using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.Drawing.Text;
using System.Collections;

public class BattleManager : MonoBehaviour
{
    static public BattleManager Instance { get; private set; }

    [Header("Enemy Health")]
    public int EnemyMaxHealth;
    public int EnemyCurrentHealth;
    private int _enemyAttack;

    [SerializeField] private HealthBar _healthBar;
    public int LastDealtDamage = 0;

    [SerializeField] private TMP_Text _bossDescriptionText;

    private int _shieldValue;

    [Header("Attack")]
    [SerializeField] private TMP_Text _totalAttackText;

    [Header("Move")]
    [SerializeField] private int _moveCountPerTurn = 3;
    [SerializeField] private int _remainingMoveCount;
    public int RemainingMoveCount => _remainingMoveCount;
    public TMP_Text MoveCounterTextField;

    [Header("Button References")]
    public Button ControlButton;
    public TMP_Text ControlButtonTextField;
    public Button ResetButton;
    public TMP_Text ResetButtonTextField;

    public IBattlePhase CurrentBattlePhase { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        ControlButton.onClick.AddListener(OnControlButtonPressed);
        // ResetButton.onClick.AddListener(OnResetButtonPressed);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            ChangePhase(new RewardPhase());
        }
    }

    public void ChangeMoveCount(int amount)
    {
        _moveCountPerTurn += amount;

        // Optionally clamp to 0 so it never goes negative
        if (_moveCountPerTurn < 0)
        {
            _moveCountPerTurn = 0;
        }
        ResetMoveCounter();
        // Debug log for clarity
        Debug.Log($"Move count changed by {amount}. New moveCountPerTurn = {_moveCountPerTurn}.");
    }

    public void StartBattleAgainstEnemy(EnemyScriptable enemyScriptable)
    {
        EnemyMaxHealth = enemyScriptable.MaxHealth;
        EnemyCurrentHealth = EnemyMaxHealth;
        _enemyAttack = enemyScriptable.Attack;

        UpdateEnemyHealth();

        if (CurrentBattlePhase != null)
        {
            Board.Instance.ClearBoard();
        }


        StartCoroutine(WaitForBoard());

        IEnumerator WaitForBoard()
        {
            yield return new WaitUntil(() => Board.Instance.GetAllSlots().Count == 9);
        }

        enemyScriptable.Initialize();
        enemyScriptable.ApplyEffect(); // TODO: Change this to only apply effects on battle start
        StartCoroutine(WaitForDeckManager());

        IEnumerator WaitForDeckManager()
        {
            yield return new WaitUntil(() => PlayerManager.Instance.Decks != null);
        }

        PlayerManager.Instance.Decks.InitializeBeforeBattle();

        _bossDescriptionText.text = enemyScriptable.Description;
        ChangePhase(new WaitPhase());
    }

    public void StartBattleAgainstEnemy(string EnemyName)
    {
        EnemyScriptable enemyScriptable = Resources.Load<EnemyScriptable>($"Enemies/{EnemyName}");
        StartBattleAgainstEnemy(enemyScriptable);
    }

    // public void SetTotalAttack()
    // {
    //     int totalAttack = Board.Instance.DeployedCards.Where(card => card != null).Sum(card => card.TotalAttack);
    //     _totalAttackText.text = $"{totalAttack}";
    // }

    public void ChangePhase(IBattlePhase newBattlePhase)
    {
        CurrentBattlePhase?.OnExit();

        CurrentBattlePhase = newBattlePhase;

        CurrentBattlePhase?.OnEnter();
    }

    public void ResetMoveCounter()
    {
        _remainingMoveCount = _moveCountPerTurn;
        if (_remainingMoveCount > 0)
        {
            GameManager.Instance.CanMove = true;
        }
        UpdateMoveCounterDisplay();
    }

    public void DecrementMoveCounter()
    {
        _remainingMoveCount--;
        if (_remainingMoveCount == 0)
        {
            GameManager.Instance.CanMove = false;
        }
        UpdateMoveCounterDisplay();
    }

    public void OnControlButtonPressed()
    {
        // TODO
        if (CurrentBattlePhase is WaitPhase)
        {
            Deploy();
            ChangePhase(new ControlPhase());
        }
        else if (CurrentBattlePhase is ControlPhase)
        {
            Attack();
            if (EnemyCurrentHealth <= 0)
            {
                ChangePhase(new RewardPhase());
            }
            else
            {
                PlayerManager.Instance.DecreaseHealth(_enemyAttack);
                if (PlayerManager.Instance.CurrentHealth <= 0)
                {
                    ChangePhase(new GameOverPhase());
                    return;
                }
                ChangePhase(new WaitPhase());
            }
        }
    }

    // public void OnResetButtonPressed()
    // {
    //     Board.Instance.DeployedCards.ForEach(card => card.ResetCardModifierState(ModifierPersistenceType.Turn));
    //     Board.Instance.ClearBoardSlotsModifiers(ModifierPersistenceType.Turn);
    //     Board.Instance.RestoreFromSnapshot();

    //     ApplyWhileInPlayEffects();

    //     ResetMoveCounter();
    // }

    private void Deploy()
    {
        Board.Instance.ClearBoard();
        Board.Instance.ClearBoardSlotsModifiers(ModifierPersistenceType.Turn);
        PlayerManager.Instance.Decks.ShuffleDrawPool();
        ResetMoveCounter();

        List<CardInstance> _cards = new();


        // Place cards randomly on the board
        while (true)
        {
            Slot slot = Board.Instance.GetRandomEmptySlot();
            if (slot == null) break;

            // Instantiate the card and bind it to the slot
            CardInstance card = PlayerManager.Instance.Decks.DrawCard();
            if (card == null) break;

            card.ResetCardModifierState(ModifierPersistenceType.Turn);
            card.BindToSlot(slot);
            card.MoveToAndSetParent(slot.ContentContainer.GetComponent<RectTransform>());

            // Apply the deploy trigger effects
            card.ActivateCardEffect(TriggerType.OnDeploy);
            _cards.Add(card);
        }
        Board.Instance.DeployedCards = _cards;
        // Board.Instance.SaveSnapshot();

        ApplyWhileInPlayEffects();
    }

    private void UpdateMoveCounterDisplay()
    {
        MoveCounterTextField.text = _remainingMoveCount.ToString();
    }

    private void Attack()
    {
        LastDealtDamage = 0;
        Board.Instance.DeployedCards.ForEach(card =>
        {
            card.ActivateCardEffect(TriggerType.BeforeAttack);
        });

        Board.Instance.DeployedCards.ForEach(card =>
        {
            card.ActivateCardEffect(TriggerType.OnAttack);
        });

        Board.Instance.DeployedCards.ForEach(card =>
        {
            card.ActivateCardEffect(TriggerType.AfterAttack);
        });
    }

    public void DealDamage(int damage)
    {
        LastDealtDamage += damage;
        EnemyCurrentHealth -= damage;

        UpdateEnemyHealth();
    }

    public void GainShield(int shieldValue)
    {
        _shieldValue += shieldValue;
    }

    private void UpdateEnemyHealth()
    {
        _healthBar.SetHealth(EnemyCurrentHealth, EnemyMaxHealth);
    }

    public void ApplyWhileInPlayEffects()
    {
        foreach (CardInstance card in Board.Instance.DeployedCards)
        {
            card.ActivateCardEffect(TriggerType.PrioWhileInPlay);
        }

        foreach (CardInstance card in Board.Instance.DeployedCards)
        {
            card.ActivateCardEffect(TriggerType.WhileInPlay);
        }
    }

    // public void RevertWhileInPlayEffects()
    // {
    //     foreach (CardView card in Board.Instance.DeployedCards)
    //     {
    //         card.RevertEffect(TriggerType.WhileInPlay);
    //     }

    //     foreach (CardView card in Board.Instance.DeployedCards)
    //     {
    //         card.RevertEffect(TriggerType.PrioWhileInPlay);
    //     }
    // }
}