using System.ComponentModel;
using System.Windows.Data;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public sealed record NavigationDestination(int Index, string Title, string Description, string Glyph, string Group);

    /// <summary>앱 관리 › 매크로. Pages added later take the next tab index so saved indices keep their pages.</summary>
    public const int MacroPageIndex = 11;

    public IReadOnlyList<NavigationDestination> Destinations { get; } =
    [
        new(0, "홈", "함대 현황과 다음 할 일을 한눈에 확인하세요.", "\uE80F", "나의 함대"),
        new(10, "편성·육성", "목표 콘텐츠를 고르면 보유 함선으로 짠 추천 편성과 다음 육성 순서를 보여 드립니다.", "\uE81E", "나의 함대"),
        new(1, "육성 계획", "내 목표부터 전투 기록까지, 함선의 성장을 관리하세요.", "\uE7C1", "나의 함대"),
        new(6, "보유 함선", "함선을 찾아 장비·스킬·육성 상태를 확인하세요.", "\uE8F1", "나의 함대"),
        new(2, "장비 창고", "보유 장비와 추천 장비를 비교하세요.", "\uE7B8", "나의 함대"),
        new(MeowfficerPageIndex, "지휘냥", "함대마다 맞는 지휘냥이 앉아 있는지, 어떤 냥을 키우고 먹일지, 다음에 배울 재능을 보여 드립니다.", MeowfficerGlyph, "나의 함대"),
        new(PersonalTaskPageIndex, "캐릭터 임무", "함선별 개인 임무를 깬 것, 진행 중인 것, 아직 시작하지 않은 것으로 나눠 보여 드립니다.", PersonalTaskGlyph, "나의 함대"),
        new(3, "함대 편성", "편성과 공격 타이밍을 함께 살펴보세요.","\uE8A9", "분석 및 운영"),
        new(4, "스킬 훈련", "훈련이 필요한 스킬과 전술 교실을 확인하세요.", "\uE7BE", "분석 및 운영"),
        new(5, "운영 현황", "의뢰·연구·건조의 진행 상태를 확인하세요.", "\uE787", "분석 및 운영"),
        new(7, "경매 분석", "관측 정보를 바탕으로 입찰 판단을 도와드립니다.", "\uE7BF", "분석 및 운영"),
        new(MonthlyShopPageIndex, "월간 상점", "이번 달 코어 월간에서 무엇부터 살지, 지금 계정에 맞춰 순서를 매겨 드립니다.", "\uE719", "분석 및 운영"),
        new(OpsiPageIndex, "대형작전", "해역별로 남은 별과 숨은 목표, 받을 보상, 세이렌 장치에 드는 아이템을 정리해 드립니다.", OpsiGlyph, "분석 및 운영"),
        new(MacroPageIndex, "매크로", "게임 화면을 보고 탭하고, 캡처한 패킷으로 결과를 확인하며 반복 작업을 실행합니다.", "\uE99A", "앱 관리"),
        new(8, "데이터 진단", "수집된 데이터와 해석 상태를 확인하세요.", "\uE9D9", "앱 관리"),
        new(9, "설정", "연결·업데이트·저장 위치를 관리하세요.", "\uE713", "앱 관리")
    ];
    public ICollectionView NavigationView { get; private set; } = null!;
    public RelayCommand NavigateCommand { get; private set; } = null!;
    /// <summary>육성 계획 › 쩔 대기열, the full growth order.</summary>
    public RelayCommand OpenGrowthQueueCommand { get; private set; } = null!;
    /// <summary>육성 계획 › 내 육성 목표.</summary>
    public RelayCommand OpenGrowthGoalsCommand { get; private set; } = null!;
    /// <summary>편성·육성 › 쫄작, the fleet built from the top of the queue.</summary>
    public RelayCommand OpenLevelingPlanCommand { get; private set; } = null!;
    public RelayCommand ResetFiltersCommand { get; private set; } = null!;
    public NavigationDestination SelectedDestination
    {
        get => Destinations.FirstOrDefault(item => item.Index == SelectedMainTabIndex) ?? Destinations[0];
        set { if (value is not null) SelectedMainTabIndex = value.Index; }
    }
    public string PageTitle => SelectedDestination.Title;
    public string PageDescription => SelectedDestination.Description;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Query) || SelectedRarityFilter != "전체" ||
        (IsShipFilterVisible && (SelectedShipTypeFilter != "전체" || SelectedNationFilter != "전체" || SelectedShipExtraFilter != "제한 없음")) ||
        (IsEquipmentFilterVisible && (SelectedEquipmentTypeFilter != "전체" || SelectedEquipmentExtraFilter != "제한 없음"));
    public string FilterSummary => HasActiveFilters ? "필터 적용 중" : "전체 보기";
    public bool HasNoShips => Ships.Count == 0;
    public string SearchHint => IsEquipmentTabActive ? "장비 이름·종류·ID 검색" : "함선 이름·함종·진영 검색";
    public bool ShowNoSearchResults => IsDockTabActive && FilteredShipCount == 0 && Ships.Count > 0;
    public string ConnectionSummary => RunningCaptureCount switch
    {
        0 => "저장된 데이터 보기",
        1 => "게임 데이터 수집 중",
        var count => $"에뮬레이터 {count}대 수집 중"
    };

    private void InitializeNavigation()
    {
        NavigationView = new ListCollectionView(Destinations.ToArray())
        {
            // The macro page runs macros from the user's own ALAuto clone. Without one it has nothing to run, so the
            // menu leaves it out (public releases ship without macros).
            Filter = item => item is not NavigationDestination { Index: MacroPageIndex } || Macros.HasAlautoRoot
        };
        NavigationView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavigationDestination.Group)));
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (int.TryParse(parameter?.ToString(), out var index) && Destinations.Any(item => item.Index == index))
                SelectedMainTabIndex = index;
        });
        OpenGrowthQueueCommand = new RelayCommand(_ =>
        {
            SelectedMainTabIndex = 1;
            SelectedLevelSectionIndex = GrowthQueueSection;
        });
        OpenGrowthGoalsCommand = new RelayCommand(_ =>
        {
            SelectedMainTabIndex = 1;
            SelectedLevelSectionIndex = GrowthGoalsSection;
        });
        OpenLevelingPlanCommand = new RelayCommand(_ =>
        {
            FleetPlanner.Select(FleetPlannerViewModel.LevelingKey);
            SelectedMainTabIndex = 10;
        });
        ResetFiltersCommand = new RelayCommand(_ =>
        {
            Query = "";
            SelectedRarityFilter = "전체";
            if (IsShipFilterVisible)
            {
                SelectedShipTypeFilter = "전체";
                SelectedNationFilter = "전체";
                SelectedShipExtraFilter = "제한 없음";
            }
            if (IsEquipmentTabActive)
            {
                SelectedEquipmentTypeFilter = "전체";
                SelectedEquipmentExtraFilter = "제한 없음";
            }
            ApplySearchRefresh();
        });
        PropertyChanged += NavigationStateChanged;
    }

    private void NavigationStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SelectedMainTabIndex) or nameof(SelectedLevelSectionIndex))
        {
            OnPropertyChanged(nameof(SelectedDestination));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageDescription));
            OnPropertyChanged(nameof(SearchHint));
        }
        if (args.PropertyName is nameof(Query) or nameof(SelectedRarityFilter) or nameof(SelectedShipTypeFilter)
            or nameof(SelectedNationFilter) or nameof(SelectedShipExtraFilter) or nameof(SelectedEquipmentTypeFilter)
            or nameof(SelectedEquipmentExtraFilter) or nameof(SelectedMainTabIndex) or nameof(SelectedLevelSectionIndex)
            or nameof(SelectedEquipmentSectionIndex))
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(FilterSummary));
        }
        if (args.PropertyName is nameof(FilteredShipCount) or nameof(SelectedMainTabIndex))
        {
            OnPropertyChanged(nameof(ShowNoSearchResults));
            OnPropertyChanged(nameof(HasNoShips));
        }
        if (args.PropertyName == nameof(IsCaptureRunning)) OnPropertyChanged(nameof(ConnectionSummary));
    }
}
