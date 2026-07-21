# Graph Report - ag-grid  (2026-07-19)

## Corpus Check
- 1 files · ~40,431 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2687 nodes · 8591 edges · 155 communities
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS · INFERRED: 18 edges (avg confidence: 0.5)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `2b2468c5`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- ag-grid-community.min.js
- onDragging
- postConstruct
- getGui
- createBean
- constructor
- R
- addOrRemoveCssClass
- destroyBean
- refreshModel
- refresh
- toggle
- createColumnEvent
- setComp
- N
- getColDef
- getParent
- getCompDetails
- getActualWidth
- redrawAfterModelUpdate
- setWidth
- addEventListener
- setToDoNothing
- m
- p
- ia
- addManagedEventListeners
- addEventListenersToPopup
- forEach
- destroy
- startEditing
- focus
- then
- push
- getNextCellToFocusWithoutCtrlPressed
- addManagedListeners
- addManagedElementListeners
- serialize
- setValue
- getHeaderCtrls
- dispatchEvent
- get
- isReadOnly
- forEachInput
- calculatePages
- getPinned
- focusGridView
- qt
- isEditing
- createPageSizeSelectorComp
- getFrameworkOverrides
- onMouseDown
- getLeft
- has
- addDestroyFunc
- E
- getRowNode
- Vi
- createOption
- checkViewportAndScrolls
- clearRowTopAndRowIndex
- isAdvancedFilterEnabled
- getColumnGroupHeaderRowHeight
- getProvidedColumnGroup
- getViewportElement
- Ko
- shouldBlockScrollUpdate
- isCheckboxSelection
- showOverlay
- getSort
- setParams
- getId
- setDragSource
- refreshCell
- refreshAriaLabelledBy
- getFloatingFilterCompDetails
- doesRowPassFilter
- ensureIndexVisible
- y
- u
- onParentModelChanged
- setupStateOnRowCountReady
- j
- onTabKeyDown
- purgeBlocksIfNeeded
- setRowTop
- export
- executeFrame
- getRow
- ze
- isLegacyMenuEnabled
- forEachNode
- getHeaderRowCount
- navigateTo
- updateStickyRows
- sort
- applyColumnState
- setDataAndId
- checkAutoHeights
- resetPlaceholder
- navigateToNextCell
- refreshCols
- focusInnerElement
- destroyFirstPass
- showOrHideSelectAll
- at
- isFilterActive
- individualConditionPasses
- getBlocksInOrder
- setupStateOnColumnsInitialised
- showPopup
- getScrollFeature
- getDataTypeDefinition
- getColumn
- extend
- createFooter
- updateLabels
- refreshNodesAndContainerHeight
- dispatchRowEvent
- getAllCols
- addParentNode
- setResizable
- processServerResult
- v
- onDisplayedColumnsChanged
- setFloatingHeights
- getCallback
- getContextMenuAnchorElement
- getInitialValues
- undoRedo
- addElementsToContainerAndGetWidth
- applyCellClassRules
- buildCompressedFileStream
- refreshHandle
- onFloatingFilterChanged
- setAutoHeightActive
- initialiseInvisibleScrollbar
- createBlock
- evaluateExpression
- processFullWidthRowKeyboardEvent
- resetIcons
- filterNodes
- initBeans
- onFullWidthContainerWheel
- syncInRowNode
- apiNotFound
- applySizeToSiblings
- calculateMouseMovement
- check
- getCellPositionForEvent
- isDomDataMissingInHierarchy
- updateGridThemeClass
- getRowByPosition
- isSuppressMenuHide
- togglePickerHasFocus

## God Nodes (most connected - your core abstractions)
1. `postConstruct()` - 207 edges
2. `forEach()` - 205 edges
3. `push()` - 139 edges
4. `setComp()` - 120 edges
5. `getGui()` - 99 edges
6. `p()` - 93 edges
7. `constructor()` - 86 edges
8. `get()` - 85 edges
9. `init()` - 83 edges
10. `getColDef()` - 79 edges

## Surprising Connections (you probably didn't know these)
- `applyColumnState()` --indirect_call--> `h()`  [INFERRED]
  ag-grid-community.min.js → ag-grid-community.min.js  _Bridges community 50 → community 96_
- `createGroups()` --indirect_call--> `h()`  [INFERRED]
  ag-grid-community.min.js → ag-grid-community.min.js  _Bridges community 50 → community 63_
- `initialiseTabGuard()` --indirect_call--> `h()`  [INFERRED]
  ag-grid-community.min.js → ag-grid-community.min.js  _Bridges community 50 → community 24_
- `setupAutoHeight()` --indirect_call--> `h()`  [INFERRED]
  ag-grid-community.min.js → ag-grid-community.min.js  _Bridges community 50 → community 54_
- `applyColumnState()` --indirect_call--> `u()`  [INFERRED]
  ag-grid-community.min.js → ag-grid-community.min.js  _Bridges community 79 → community 96_

## Import Cycles
- None detected.

## Communities (155 total, 0 thin omitted)

### Community 0 - "ag-grid-community.min.js"
Cohesion: 0.01
Nodes (32): areModelsEqual(), areSimpleModelsEqual(), calculateBounds(), calculatePixelOffset(), checkForDoubleTap(), equals(), expandRows(), getFirstVirtualRenderedRow() (+24 more)

### Community 1 - "onDragging"
Cohesion: 0.05
Nodes (71): addRowDropZone(), allContainersIntersect(), attemptToPinColumns(), checkCenterForScrolling(), clearColumnsList(), clearDragAndDropProperties(), clearHighlighted(), clearRowHighlight() (+63 more)

### Community 2 - "postConstruct"
Cohesion: 0.06
Nodes (41): addGlobalListener(), createTemplate(), destroyStickyCtrls(), disableFeature(), enableFeature(), getContainerElement(), getMaxConcurrentDatasourceRequests(), getPositionableElement() (+33 more)

### Community 3 - "getGui"
Cohesion: 0.08
Nodes (40): addCssClass(), addInCellEditor(), addRowNodes(), afterCellRendererCreated(), appendChild(), ba(), Br(), clearParentOfValue() (+32 more)

### Community 4 - "createBean"
Cohesion: 0.07
Nodes (39): addExistingKeys(), addPopupCellEditor(), addRowDraggerToRow(), applyElementsToComponent(), balanceColumnTree(), balanceTreeForAutoCols(), createBean(), createColumn() (+31 more)

### Community 5 - "constructor"
Cohesion: 0.07
Nodes (37): activateTabIndex(), ar(), calculateRowLevel(), constructor(), dispatchQueuedStateUpdateEvents(), getCallbackForEvent(), getCellAriaRole(), getCompId() (+29 more)

### Community 6 - "R"
Cohesion: 0.07
Nodes (36): addGridCommonParams(), ai(), applyUserStyles(), createBaseColDefParams(), createCellRendererParams(), createGlobalRowEvent(), createRowDragComp(), dispatchCellChangedEvent() (+28 more)

### Community 7 - "addOrRemoveCssClass"
Cohesion: 0.09
Nodes (36): addOrRemoveCssClass(), announceDescription(), C(), checkVisibility(), executeSlideAndFadeAnimations(), forEachGui(), getFirstRow(), getInitialRowClasses() (+28 more)

### Community 8 - "destroyBean"
Cohesion: 0.08
Nodes (34): addControls(), afterCellEditorCreated(), afterCompCreated(), afterHeaderCompCreated(), createCellEditorInstance(), createCellRendererInstance(), createDragAndDropImageComponent(), destroyBean() (+26 more)

### Community 9 - "refreshModel"
Cohesion: 0.06
Nodes (32): afterImmutableDataChange(), batchUpdateRowData(), buildRefreshModelParams(), commonUpdateRowData(), createChangePath(), dispatchSortChangedEvents(), dispatchUpdateEventsAndRefresh(), doAggregate() (+24 more)

### Community 10 - "refresh"
Cohesion: 0.08
Nodes (32): attemptHeaderCompRefresh(), calculateDisplayName(), checkDisplayName(), createFloatingFilterInputService(), createValueElement(), hideDeltaValue(), joinCols(), Lo() (+24 more)

### Community 11 - "toggle"
Cohesion: 0.08
Nodes (31): Fr(), getCurrentPage(), getDomLayout(), getNextValue(), getScrollbarWidth(), ir(), Mr(), Na() (+23 more)

### Community 12 - "createColumnEvent"
Cohesion: 0.10
Nodes (30): A(), addPivotColumns(), addRowGroupColumns(), addValueColumns(), createColumnEvent(), createGroupSafeValueFormatter(), extractCols(), extractColsCommon() (+22 more)

### Community 13 - "setComp"
Cohesion: 0.11
Nodes (30): addColumnHoverListener(), addDropTarget(), addRowDragListener(), applyStaticCssClasses(), areFilterCompsDifferent(), createManagedBean(), createParams(), isHovered() (+22 more)

### Community 14 - "N"
Cohesion: 0.12
Nodes (30): calculateSelectedFromChildren(), clearOtherNodes(), depthFirstSearchChangedPath(), depthFirstSearchEverything(), deselectAllRowNodes(), dispatchSelectionChanged(), forEachChangedNodeDepthFirst(), forEachNodeOnPage() (+22 more)

### Community 15 - "getColDef"
Cohesion: 0.09
Nodes (30): compareRowNodes(), executeFilterValueGetter(), executeValueGetter(), getColDef(), getColId(), getColumnStateFromColDef(), getComparator(), getDeleteValue() (+22 more)

### Community 16 - "getParent"
Cohesion: 0.11
Nodes (29): addClasses(), autoSizeColumnGroupsByColumns(), doAddHeaderHeader(), findChildrenRemovingPadding(), findColAtEdgeForHeaderRow(), findGroupWidthId(), findHeader(), focusHeader() (+21 more)

### Community 17 - "getCompDetails"
Cohesion: 0.08
Nodes (29): addFullWidthRowDragging(), createFullWidthCompDetails(), getCellEditorDetails(), getCellRendererDetails(), getCompDetails(), getCompKeys(), getDateCompDetails(), getDragAndDropImageCompDetails() (+21 more)

### Community 18 - "getActualWidth"
Cohesion: 0.11
Nodes (29): applyAutosizeStrategy(), autoSizeAllColumns(), autoSizeCols(), autoSizeColumn(), checkMinAndMaxWidthsForSet(), extractDataFromEvent(), fireColumnWidthChangedEvent(), getActualWidth() (+21 more)

### Community 19 - "redrawAfterModelUpdate"
Cohesion: 0.12
Nodes (29): datasourceChanged(), destroyRowCtrls(), destroySecondPass(), dispatchDisplayedRowsChanged(), ensureAllRowsInRangeHaveHeightsCalculated(), getCellToRestoreFocusToAfterRefresh(), getLockOnRefresh(), getRowBuffer() (+21 more)

### Community 20 - "setWidth"
Cohesion: 0.12
Nodes (28): center(), constrainSizeToAvailableHeight(), findBoundaryElement(), getAvailableHeight(), getContainerWidth(), getHeight(), getWidth(), getWidthForRow() (+20 more)

### Community 21 - "addEventListener"
Cohesion: 0.10
Nodes (27): addColumnListeners(), addEventListener(), addKeyboardModeEvents(), addListenersToChildrenColumns(), addRenderedRowListener(), createBodyTemplate(), dispatchAsync(), dispatchToListeners() (+19 more)

### Community 22 - "setToDoNothing"
Cohesion: 0.10
Nodes (27): addHoverFunctionality(), clearHideTimeout(), clearInteractiveTimeout(), clearShowTimeout(), clearTimeouts(), clearTooltipListeners(), destroyTooltipComp(), getGridOptionsTooltipDelay() (+19 more)

### Community 23 - "m"
Cohesion: 0.11
Nodes (26): addFunction(), __assertRegistered(), cd(), create(), createBeansList(), createProvidedBeans(), extractModuleEntity(), getBean() (+18 more)

### Community 24 - "p"
Cohesion: 0.09
Nodes (26): addTabGuards(), canInferCellDataType(), checkCompatibility(), checkWarnings(), doColDefPropsPreventInference(), doesColDefPropPreventInference(), dt(), getInitialData() (+18 more)

### Community 25 - "ia"
Cohesion: 0.11
Nodes (25): Aa(), addOption(), ca(), createTabGuard(), ea(), fa(), ga(), ha() (+17 more)

### Community 26 - "addManagedEventListeners"
Cohesion: 0.14
Nodes (25): addEventListeners(), addInIcon(), addInputListeners(), addListeners(), addManagedEventListeners(), addManagedPropertyListener(), addManagedPropertyListeners(), addPropertyListeners() (+17 more)

### Community 27 - "addEventListenersToPopup"
Cohesion: 0.14
Nodes (25): addEventListenersToPopup(), addPopup(), addPopupToPopupList(), bringPopupToFront(), calculatePointerAlign(), callPostProcessPopup(), createPopupWrapper(), getParentRect() (+17 more)

### Community 28 - "forEach"
Cohesion: 0.13
Nodes (25): addFolders(), addListenersForCellComps(), createXml(), destroyColumnStateUpdateListeners(), flushAsyncQueue(), forEach(), forEachLeafNode(), getAllCellCtrls() (+17 more)

### Community 29 - "destroy"
Cohesion: 0.11
Nodes (25): clear(), clearLocalValues(), clearOptions(), destroy(), destroyAllCells(), destroyBeans(), destroyCells(), destroyDatasource() (+17 more)

### Community 30 - "startEditing"
Cohesion: 0.14
Nodes (25): findNextCellToFocusOn(), focusCell(), getCellByPosition(), getCellEditor(), getCellPosition(), getComp(), getLastCellOfColSpan(), getRowCtrl() (+17 more)

### Community 31 - "focus"
Cohesion: 0.12
Nodes (24): activateTabGuards(), afterGuiAttached(), alignPickerToComponent(), clearRestoreFocus(), createPickerComponent(), er(), focus(), focusIn() (+16 more)

### Community 32 - "then"
Cohesion: 0.14
Nodes (23): ae(), afterInit(), all(), destroyFilter(), disableColumnFilters(), disposeColumnListener(), disposeFilterWrapper(), forEachColumnFilter() (+15 more)

### Community 33 - "push"
Cohesion: 0.12
Nodes (23): buildColumnDefs(), canSkip(), createDefFromGroup(), createHeader(), doDeltaSort(), getColumnGroupState(), getCSS(), _getCSSChunks() (+15 more)

### Community 34 - "getNextCellToFocusWithoutCtrlPressed"
Cohesion: 0.09
Nodes (23): createColumnFunctionCallbackParams(), getCellAbove(), getCellBelow(), getCellToLeft(), getCellToRight(), getLastBodyCell(), getLastFloatingTopRow(), getNextCellToFocus() (+15 more)

### Community 35 - "addManagedListeners"
Cohesion: 0.12
Nodes (22): addActiveHeaderMouseListeners(), addHeaderMouseListeners(), addHighlightListeners(), addManagedListeners(), addMouseHoverListeners(), addResizeAndMoveKeyboardListeners(), configureFilter(), dispatchColumnMouseEvent() (+14 more)

### Community 36 - "addManagedElementListeners"
Cohesion: 0.12
Nodes (22): addBodyViewportListener(), addFocusListeners(), addFullWidthContainerWheelListener(), addHorizontalScrollListeners(), addKeyboardListeners(), addKeyDownListeners(), addManagedElementListeners(), addMouseListeners() (+14 more)

### Community 37 - "serialize"
Cohesion: 0.11
Nodes (22): addCustomContent(), appendContent(), appendEmptyCells(), beginNewLine(), exportHeaders(), extractHeaderValue(), getColumnsToExport(), getHeaderName() (+14 more)

### Community 38 - "setValue"
Cohesion: 0.13
Nodes (22): adjustPrecision(), createListComponent(), dispatchChange(), dispatchLocalEvent(), fireChangeEvent(), fireItemSelected(), getActiveInputElement(), hidePicker() (+14 more)

### Community 39 - "getHeaderCtrls"
Cohesion: 0.12
Nodes (22): areCellsRendered(), findHeaderCellCtrl(), getActualDepth(), getColumnGroupChild(), getColumnsInViewport(), getColumnsInViewportNormalLayout(), getColumnsInViewportPrintLayout(), getHeaderCellCtrl() (+14 more)

### Community 40 - "dispatchEvent"
Cohesion: 0.14
Nodes (22): clearMouseOver(), createEvent(), createRowEventWithSource(), dispatchCellContextMenuEvent(), dispatchEvent(), dispatchEventOnce(), fe(), isDoubleClickOnIPad() (+14 more)

### Community 41 - "get"
Cohesion: 0.11
Nodes (21): addFeatures(), ce(), createMethod(), createMethodProxy(), de(), enableTooltipFeature(), get(), he() (+13 more)

### Community 42 - "isReadOnly"
Cohesion: 0.17
Nodes (21): applyModel(), getJoinOperator(), getModelFromUi(), isConditionDisabled(), isModelValid(), isReadOnly(), onUiChanged(), resetInput() (+13 more)

### Community 43 - "forEachInput"
Cohesion: 0.14
Nodes (20): addChangedListeners(), attachElementOnChange(), Bo(), createCondition(), forEachInput(), forEachPositionInput(), forEachPositionTypeInput(), getConditionType() (+12 more)

### Community 44 - "calculatePages"
Cohesion: 0.14
Nodes (20): adjustCurrentPageIfInvalid(), calculatedPagesNotActive(), calculatePages(), calculatePagesAllRows(), calculatePagesMasterRowsOnly(), dispatchPaginationChangedEvent(), getPageForIndex(), goToFirstPage() (+12 more)

### Community 45 - "getPinned"
Cohesion: 0.16
Nodes (20): columnPinned(), columnVisible(), compareColumnStatesAndDispatchEvents(), createDefFromColumn(), createStateItemFromColumn(), getCommonValue(), getPinned(), getSortIndex() (+12 more)

### Community 46 - "focusGridView"
Cohesion: 0.17
Nodes (20): findFocusableElementBeforeTabGuard(), findFocusableElements(), findNextFocusableElement(), focusAdvancedFilter(), focusFirstHeader(), focusGridView(), focusGridViewFailed(), focusHeaderPosition() (+12 more)

### Community 47 - "qt"
Cohesion: 0.16
Nodes (19): addDisplayedLeafColumns(), addLeafColumns(), calculateDisplayedColumns(), calculateHeaderRows(), checkLeft(), getAllTrees(), getColumnGroup(), getDisplayedChildren() (+11 more)

### Community 48 - "isEditing"
Cohesion: 0.15
Nodes (19): addKeyDownListener(), doGridOperations(), getControlsForEventTarget(), isDisplayed(), isEditing(), onCtrlAndC(), onCtrlAndD(), onCtrlAndV() (+11 more)

### Community 49 - "createPageSizeSelectorComp"
Cohesion: 0.13
Nodes (19): addOptions(), applyThemeClasses(), createPageSizeSelectOptions(), createPageSizeSelectorComp(), fireGridStylesChangedEvent(), getPageSizeSelectorValues(), getSizeEl(), getThemeClasses() (+11 more)

### Community 50 - "getFrameworkOverrides"
Cohesion: 0.13
Nodes (19): addSlideAnimation(), callColumnCellValueChangedHandler(), checkForRequiredDependencies(), checkProperties(), fireColumnEvent(), fireEvent(), frameworkComponent(), getAlignedGridApis() (+11 more)

### Community 51 - "onMouseDown"
Cohesion: 0.15
Nodes (19): addTemporaryEvents(), cancelDrag(), containsWidget(), getActiveTouch(), getFirstActiveTouch(), isEventNearStartEvent(), isOverFormFieldElement(), isRightClickInExistingRange() (+11 more)

### Community 52 - "getLeft"
Cohesion: 0.13
Nodes (19): animateInLeft(), executeNextVMTurn(), findFirstAndLastMovingColumns(), getBodyContainerWidth(), getCellLeft(), getColsLeftWidth(), getColumnMoveAndTargetInfo(), getColumnOrGroup() (+11 more)

### Community 53 - "has"
Cohesion: 0.15
Nodes (18): addColumnDefaultAndTypes(), applyGlobalGridOptions(), assignColumnTypes(), ct(), generateColumnStateForRowGroupAndPivotIndexes(), getById(), getIds(), has() (+10 more)

### Community 54 - "addDestroyFunc"
Cohesion: 0.12
Nodes (18): addDestroyFunc(), addDomData(), addPreventScrollWhileDragging(), checkRightRowModelType(), dispatchFirstDataRenderedEvent(), G(), observeResize(), onRowHeightChanged() (+10 more)

### Community 55 - "E"
Cohesion: 0.15
Nodes (18): areAllContainersReady(), before(), checkGenerateQuickFilterAggregateText(), doesRowPassQuickFilter(), doesRowPassQuickFilterCache(), doesRowPassQuickFilterMatcher(), doesRowPassQuickFilterNoCache(), E() (+10 more)

### Community 56 - "getRowNode"
Cohesion: 0.16
Nodes (18): calculateIndexesToDraw(), doNotUnVirtualiseRow(), forEachPinnedRow(), getCellRenderer(), getCellRendererInstances(), getFullWidthCellRenderers(), getFullWidthRowCtrls(), getPinnedRowById() (+10 more)

### Community 57 - "Vi"
Cohesion: 0.14
Nodes (18): doesMovePassLockedPositions(), doesMovePassMarryChildren(), doesMovePassRules(), doesOrderPassRules(), findExistingGroup(), getCenterCols(), getColGroupDef(), getColumnForFullWidth() (+10 more)

### Community 58 - "createOption"
Cohesion: 0.28
Nodes (17): afterGuiDetached(), createJoinOperatorPanel(), createMissingConditionsAndOperators(), createOption(), getNumConditions(), getUiCompleteConditions(), isConditionUiComplete(), isValidDateValue() (+9 more)

### Community 59 - "checkViewportAndScrolls"
Cohesion: 0.12
Nodes (17): checkBodyHeight(), checkViewportAndScrolls(), getPinnedColumnsOverflowingViewport(), hasHorizontalScrollGap(), hasVerticalScrollGap(), isHorizontalScrollShowing(), isVerticalScrollShowing(), keepPinnedColumnsNarrowerThanViewport() (+9 more)

### Community 60 - "clearRowTopAndRowIndex"
Cohesion: 0.15
Nodes (17): clearRowTopAndRowIndex(), createNode(), createTransactionForRowData(), dispatchRowDataUpdateStartedEvent(), executeAdd(), executeRemove(), executeUpdate(), getCopyOfNodesMap() (+9 more)

### Community 61 - "isAdvancedFilterEnabled"
Cohesion: 0.21
Nodes (17): getAdvancedFilterModel(), getColumnFilterInstance(), getFilterInstance(), getFilterInstanceImpl(), getFilterModel(), getOrCreateFilterWrapper(), Gn(), isAdvancedFilterEnabled() (+9 more)

### Community 62 - "getColumnGroupHeaderRowHeight"
Cohesion: 0.17
Nodes (17): getAutoHeaderHeight(), getColumnGroupHeaderRowHeight(), getColumnHeaderRowHeight(), getFloatingFiltersHeight(), getGroupHeaderHeight(), getGroupRowCount(), getGroupRowCtrlAtIndex(), getGroupRowsHeight() (+9 more)

### Community 63 - "getProvidedColumnGroup"
Cohesion: 0.16
Nodes (16): addChild(), buildTrees(), createColGroup(), createGroups(), getColGroupAtLevel(), getDisplayNameForColumnGroup(), getDisplayNameForProvidedColumnGroup(), getGroupAtDirection() (+8 more)

### Community 64 - "getViewportElement"
Cohesion: 0.14
Nodes (16): attemptSettingScrollPosition(), checkViewportColumns(), extractViewport(), getBodyViewportElement(), getHeaderRowContainerCtrl(), getScrollPosition(), getViewportColumns(), getViewportElement() (+8 more)

### Community 65 - "Ko"
Cohesion: 0.13
Nodes (16): checkObjectValueHandlers(), finish(), flush(), getDate(), getDefaultDataTypes(), Ko(), lr(), O() (+8 more)

### Community 66 - "shouldBlockScrollUpdate"
Cohesion: 0.16
Nodes (16): checkScrollLeft(), doHorizontalScroll(), fireScrollEvent(), getViewportForSource(), horizontallyScrollHeaderCenterAndFloatingCenter(), isControllingScroll(), onHScroll(), onVScroll() (+8 more)

### Community 67 - "isCheckboxSelection"
Cohesion: 0.15
Nodes (16): checkSelectionType(), createControlsCols(), ge(), getIsVisible(), isCellCheckboxSelection(), isCheckboxSelection(), isControlsColEnabled(), isIncludeControl() (+8 more)

### Community 68 - "showOverlay"
Cohesion: 0.17
Nodes (16): destroyActiveOverlay(), doHideOverlay(), doShowLoadingOverlay(), doShowNoRowsOverlay(), hideOverlay(), isExclusive(), isGridFocused(), onGridSizeChanged() (+8 more)

### Community 69 - "getSort"
Cohesion: 0.21
Nodes (15): canColumnDisplayMixedSort(), clearSortBarTheseColumns(), getColumnsWithSortingOrdered(), getDisplaySortForColumn(), getDisplaySortIndexForColumn(), getIndexedSortMap(), getNextSortDirection(), getSort() (+7 more)

### Community 70 - "setParams"
Cohesion: 0.17
Nodes (15): checkApplyDebounce(), close(), getDefaultFilterOptions(), getDefaultJoinOperator(), getModel(), getTextMatcher(), handleCancelEnd(), onBtApply() (+7 more)

### Community 71 - "getId"
Cohesion: 0.16
Nodes (15): checkRowCount(), clearFocusedCell(), createValueForGroupNode(), extractRowCellValue(), getFocusEventParams(), getId(), getNotValueColumnsForNode(), getProvidedColGroup() (+7 more)

### Community 72 - "setDragSource"
Cohesion: 0.15
Nodes (14): addDragSource(), addGuiEventListener(), clearComponent(), createDragItem(), getAllMovingColumns(), getDragItem(), getRowDragText(), getSelectedNodes() (+6 more)

### Community 73 - "refreshCell"
Cohesion: 0.14
Nodes (14): animateCell(), callValueFormatter(), createId(), createIdFromValues(), flashCell(), flashCells(), formatValue(), isSuppressFlashingCellsBecauseFiltering() (+6 more)

### Community 74 - "refreshAriaLabelledBy"
Cohesion: 0.19
Nodes (14): beforeHidePicker(), createDateComponent(), da(), getAriaElement(), getAriaLabel(), getLabel(), refreshAriaLabelledBy(), setAriaLabel() (+6 more)

### Community 75 - "getFloatingFilterCompDetails"
Cohesion: 0.19
Nodes (14): callOnFilterChangedOutsideRenderCycle(), checkDestroyFilter(), createFilterInstance(), createFilterParams(), createFilterWrapper(), createGetValue(), createValueGetter(), filterChangedCallbackFactory() (+6 more)

### Community 76 - "doesRowPassFilter"
Cohesion: 0.23
Nodes (14): doesExternalFilterPass(), doesRowPassAggregateFilters(), doesRowPassFilter(), doesRowPassOtherFilters(), isAdvancedFilterPresent(), isAggregateFilterPresent(), isAggregateQuickFilterPresent(), isAnyFilterPresent() (+6 more)

### Community 77 - "ensureIndexVisible"
Cohesion: 0.18
Nodes (14): ensureCellVisible(), ensureColumnVisible(), ensureIndexVisible(), ensureNodeVisible(), getGridBodyCtrl(), getResizeDiff(), getStickyBottomHeight(), getStickyTopHeight() (+6 more)

### Community 78 - "y"
Cohesion: 0.20
Nodes (13): getDateComponentParams(), getDefaultDebounceMs(), getDomDataKey(), k(), mo(), qo(), setRowAutoHeight(), setTextInputParams() (+5 more)

### Community 79 - "u"
Cohesion: 0.19
Nodes (13): calculateOffset(), clearOffset(), getAvailableLoadingCount(), insertGridIntoDom(), load(), loadFromDatasource(), performCheckBlocksToLoad(), printCacheStatus() (+5 more)

### Community 80 - "onParentModelChanged"
Cohesion: 0.19
Nodes (13): canWeEditAfterModelFromParentFilter(), conditionToString(), doesFilterHaveSingleInput(), getFilterModelFormatter(), getModelAsString(), isEventFromDataChange(), isEventFromFloatingFilter(), isTypeEditable() (+5 more)

### Community 81 - "setupStateOnRowCountReady"
Cohesion: 0.15
Nodes (13): cc(), getFilterState(), getInitialState(), getPaginationState(), getRowGroupExpansionState(), getRowSelectionState(), refreshStaleState(), setCachedStateValue() (+5 more)

### Community 82 - "j"
Cohesion: 0.17
Nodes (13): checkPageSize(), getBodyHeight(), getCSSVariablePixelValue(), getDefaultColumnMinWidth(), getDefaultHeaderHeight(), getDefaultListItemHeight(), getDefaultRowHeight(), initMinAndMaxWidths() (+5 more)

### Community 83 - "onTabKeyDown"
Cohesion: 0.17
Nodes (13): createCellPosition(), deactivateTabGuards(), getNextFocusableElement(), getRowIndex(), getRowPosition(), getTooltipParams(), getTooltipText(), isRowFocused() (+5 more)

### Community 84 - "purgeBlocksIfNeeded"
Cohesion: 0.22
Nodes (13): createLoadParams(), getBlockState(), getBlockStateJson(), getEndRow(), getLastAccessed(), getSideBarState(), getStartRow(), getState() (+5 more)

### Community 85 - "setRowTop"
Cohesion: 0.24
Nodes (13): createRowNodes(), ensureRowHeightsValid(), onGridStylesChanges(), onPaginationPixelOffsetChanged(), onTopChanged(), resetRowHeights(), resetRowHeightsForAllRowNodes(), setRowHeight() (+5 more)

### Community 86 - "export"
Cohesion: 0.18
Nodes (13): createSerializingSession(), download(), Eo(), export(), exportDataAsCsv(), getData(), getDataAsCsv(), getDefaultFileExtension() (+5 more)

### Community 87 - "executeFrame"
Cohesion: 0.20
Nodes (12): addDestroyTask(), addTaskToList(), createTask(), debounce(), executeFrame(), flushAllFrames(), isOn(), requestFrame() (+4 more)

### Community 88 - "getRow"
Cohesion: 0.24
Nodes (12): addRow(), clearHighlightedRow(), ensureRowsAtPixel(), getHighlightPosition(), getRow(), getRowIndexAtPixel(), highlightRowAtPixel(), isHighlightingCurrentPosition() (+4 more)

### Community 89 - "ze"
Cohesion: 0.21
Nodes (12): addSelectionHandle(), getCols(), getDisplayedLeafColumns(), getFirstColumn(), highlightHoveredColumn(), isColAtEdge(), ni(), pe() (+4 more)

### Community 90 - "isLegacyMenuEnabled"
Cohesion: 0.20
Nodes (12): areAdditionalColumnMenuItemsEnabled(), getColumnMenuType(), isColumnMenuAnchoringEnabled(), isColumnMenuInHeaderEnabled(), isFilterMenuInHeaderEnabled(), isFilterMenuItemEnabled(), isFloatingFilterButtonDisplayed(), isFloatingFilterButtonEnabled() (+4 more)

### Community 91 - "forEachNode"
Cohesion: 0.20
Nodes (12): dispatchModelUpdatedEvent(), forEachNode(), forEachNodeAfterFilter(), forEachNodeAfterFilterAndSort(), forEachNodeDeep(), forEachPivotNode(), getNodesInRangeForSelection(), getRowNodesInRange() (+4 more)

### Community 92 - "getHeaderRowCount"
Cohesion: 0.20
Nodes (12): focusNextHeaderRow(), getHeaderRowCount(), getPinnedBottomRowCount(), getRowCount(), getTopLevelRowCount(), isAdvancedFilterHeaderActive(), isEmpty(), isRowsToRender() (+4 more)

### Community 93 - "navigateTo"
Cohesion: 0.29
Nodes (12): getNextFocusIndexForAutoHeight(), getViewportHeight(), handlePageScrollingKey(), handlePageUpDown(), isRowTallerThanView(), navigateTo(), navigateToNextPage(), navigateToNextPageWithAutoHeight() (+4 more)

### Community 94 - "updateStickyRows"
Cohesion: 0.25
Nodes (11): addGroupExpandIcon(), areFooterRowsStickySuppressed(), canRowsBeSticky(), getClientSideLastPixelOfGroup(), getFirstPixelOfGroup(), getLastPixelOfGroup(), getServerSideLastPixelOfGroup(), getStickyAncestors() (+3 more)

### Community 95 - "sort"
Cohesion: 0.20
Nodes (11): announceAriaDescription(), announceValue(), calculateDirtyNodes(), doFullSort(), getColumnDefs(), getFirstChildOfFirstChild(), pullDownGroupDataForHideOpenParents(), sort() (+3 more)

### Community 96 - "applyColumnState"
Cohesion: 0.35
Nodes (11): applyColumnState(), calculateColInitialWidth(), D(), dispatchStateUpdatedEvent(), orderLiveColsLikeState(), setFlex(), setPinned(), setSort() (+3 more)

### Community 97 - "setDataAndId"
Cohesion: 0.22
Nodes (11): be(), checkRowSelectable(), createDaemonNode(), createDataChangedEvent(), expire(), onDataChanged(), onSelectionChanged(), resetQuickFilterAggregateText() (+3 more)

### Community 98 - "checkAutoHeights"
Cohesion: 0.24
Nodes (11): checkAutoHeights(), createAllCellCtrls(), extractViewportColumns(), getAllAutoHeightCols(), getColsForRow(), getColsWithinViewport(), getLeftColsForRow(), getRightColsForRow() (+3 more)

### Community 99 - "resetPlaceholder"
Cohesion: 0.25
Nodes (11): createBoilerplateListOption(), createCustomListOption(), createFilterListOptions(), getFilterTitle(), getPlaceholderText(), isDefaultOperator(), resetJoinOperator(), resetJoinOperatorAnd() (+3 more)

### Community 100 - "navigateToNextCell"
Cohesion: 0.20
Nodes (11): findFullWidthRowGui(), focusPosition(), getFocusedCell(), isValidNavigateCell(), navigateToNextCell(), onKeyboardNavigate(), onNavigationKeyDown(), onRowMouseDown() (+3 more)

### Community 101 - "refreshCols"
Cohesion: 0.22
Nodes (10): addAutoCols(), addControlsCols(), createColsFromColDefs(), positionLockedCols(), recreateColumnDefs(), refreshCols(), saveColOrder(), selectCols() (+2 more)

### Community 102 - "focusInnerElement"
Cohesion: 0.24
Nodes (10): allowFocusForNextCoreContainer(), findNextElementOutsideAndFocus(), focusContainer(), focusInnerElement(), focusNextInnerContainer(), getFocusableContainers(), getNextFocusableIndex(), onFocus() (+2 more)

### Community 103 - "destroyFirstPass"
Cohesion: 0.24
Nodes (10): applyPaginationOffset(), destroyFirstPass(), getApproximateVScollPosition(), getInitialRowTop(), getInitialRowTopShared(), getInitialTransform(), getPixelOffset(), getRealPixelPosition() (+2 more)

### Community 104 - "showOrHideSelectAll"
Cohesion: 0.24
Nodes (10): applyRowSpan(), isCurrentPageOnly(), isFilteredOnly(), onCbSelectAll(), onModelChanged(), onNewColumnsLoaded(), refreshSelectAllLabel(), setupRowSpan() (+2 more)

### Community 105 - "at"
Cohesion: 0.22
Nodes (10): at(), createAutoCols(), getColsToShow(), ie(), isNodeFullWidthCell(), isPivotMode(), isShowingPivotResult(), isSuppressAutoCol() (+2 more)

### Community 106 - "isFilterActive"
Cohesion: 0.27
Nodes (10): cachedFilter(), getColumnFilterModel(), getCurrentFloatingFilterParentModel(), getFilterWrapper(), getModelFromFilterWrapper(), getModelFromInitialState(), isFilterActive(), onFilterChangedButton() (+2 more)

### Community 107 - "individualConditionPasses"
Cohesion: 0.20
Nodes (10): comparator(), doAggregateFiltersPass(), doColumnFiltersPass(), doesFilterPass(), evaluateCustomFilter(), evaluateNonNullValue(), evaluateNullValue(), getCellValue() (+2 more)

### Community 108 - "getBlocksInOrder"
Cohesion: 0.27
Nodes (10): destroyAllBlocksPastVirtualRowCount(), destroyBlock(), getBlocksInOrder(), onCacheUpdated(), purgeCache(), refreshCache(), removeBlock(), removeBlockFromCache() (+2 more)

### Community 109 - "setupStateOnColumnsInitialised"
Cohesion: 0.24
Nodes (10): dispatchStateUpdateEvent(), getColumnState(), orderColumnStateList(), setColumnGroupState(), setColumnState(), setupStateOnColumnsInitialised(), suppressEventsAndDispatchInitEvent(), T() (+2 more)

### Community 110 - "showPopup"
Cohesion: 0.22
Nodes (10): dispatchVisibleChangedEvent(), getAnchorElementForMenu(), hasFilter(), setMenuVisible(), showColumnMenu(), showColumnMenuCommon(), showMenuAfterButtonClick(), showMenuAfterMouseEvent() (+2 more)

### Community 111 - "getScrollFeature"
Cohesion: 0.31
Nodes (10): getHScrollPosition(), getNormalisedPosition(), getScrollFeature(), getScrollPositionForPixel(), getScrollState(), getUiBodyHeight(), getVScrollPosition(), onEvent() (+2 more)

### Community 112 - "getDataTypeDefinition"
Cohesion: 0.22
Nodes (9): checkType(), formatDate(), getBaseDataType(), getDataTypeDefinition(), getDateFormatterFunction(), getDateParserFunction(), getDateStringTypeDefinition(), getStartValue() (+1 more)

### Community 113 - "getColumn"
Cohesion: 0.28
Nodes (9): createCellCtrls(), createCellEditorParams(), getColumn(), getEditCompDetails(), handleColDefChanged(), isCellEligibleToBeRemoved(), isCellFocused(), isCellFocusSuppressed() (+1 more)

### Community 114 - "extend"
Cohesion: 0.28
Nodes (9): depthFirstSearch(), extend(), getEnd(), getRange(), getRoot(), isInRange(), setEndRange(), setRoot() (+1 more)

### Community 115 - "createFooter"
Cohesion: 0.32
Nodes (8): addRowNodeToRowsToDisplay(), createDetailNode(), createFooter(), destroyFooter(), execute(), getFlattenDetails(), recursivelyAddToRowsToDisplay(), setUiLevel()

### Community 116 - "updateLabels"
Cohesion: 0.32
Nodes (8): announceAriaStatus(), enableOrDisableButtons(), formatNumber(), isZeroPagesToDisplay(), jo(), setTotalLabelsToZero(), Uo(), updateLabels()

### Community 117 - "refreshNodesAndContainerHeight"
Cohesion: 0.29
Nodes (8): checkStickyRows(), createOrUpdateRowCtrl(), createRowCon(), refreshNodesAndContainerHeight(), refreshStickyNode(), resetOffsets(), setOffsetBottom(), setOffsetTop()

### Community 118 - "dispatchRowEvent"
Cohesion: 0.36
Nodes (8): dispatchRowEvent(), setAllChildrenCount(), setChildIndex(), setFirstChild(), setGroup(), setLastChild(), updateChildIndexes(), updateHasChildren()

### Community 119 - "getAllCols"
Cohesion: 0.32
Nodes (8): findNextColumnWithFloatingFilter(), getAllCols(), getColAfter(), getColBefore(), getDragItemForGroup(), getRangeBorders(), isColDisplayed(), sameRow()

### Community 120 - "addParentNode"
Cohesion: 0.33
Nodes (7): addParentNode(), createPathItems(), doChangeDetection(), isRowPinned(), linkPathItems(), onCellValueChanged(), populateColumnsMap()

### Community 121 - "setResizable"
Cohesion: 0.29
Nodes (7): addResizers(), clearResizeListeners(), createResizeMap(), getResizerElement(), onResizeEnd(), removeResizers(), setResizable()

### Community 122 - "processServerResult"
Cohesion: 0.33
Nodes (7): dispatchLoadCompleted(), isRequestMostRecentAndLive(), pageLoadFailed(), processServerFail(), processServerResult(), success(), successCommon()

### Community 123 - "v"
Cohesion: 0.29
Nodes (7): f(), getRowDragFeature(), hasExternalDropZones(), onSuppressRowDrag(), setDisplayedOrVisible(), v(), workOutVisibility()

### Community 124 - "onDisplayedColumnsChanged"
Cohesion: 0.38
Nodes (7): forContainers(), getAriaColIndex(), onDisplayedColumnsChanged(), onDisplayedColumnsWidthChanged(), refreshAriaColIndex(), updateScrollVisible(), wasAutoRowHeightEverActive()

### Community 125 - "setFloatingHeights"
Cohesion: 0.33
Nodes (7): getByIndex(), getPinnedBottomTotalHeight(), getPinnedTopTotalHeight(), getSize(), getTotalHeight(), setFloatingHeights(), setStickyBottomOffsetBottom()

### Community 126 - "getCallback"
Cohesion: 0.29
Nodes (7): getCallback(), isFullWidthCell(), isModuleRegistered(), mergeGridCommonParams(), ne(), se(), setId()

### Community 127 - "getContextMenuAnchorElement"
Cohesion: 0.29
Nodes (7): getCellGui(), getContextMenuAnchorElement(), getContextMenuPosition(), getFullWidthElement(), getGridBodyElement(), getRowYPosition(), showContextMenu()

### Community 128 - "getInitialValues"
Cohesion: 0.33
Nodes (7): getColDefValue(), getColumnsToResize(), getInitialSizeOfColumns(), getInitialValues(), getSizeRatiosOfColumns(), isResizable(), isSortable()

### Community 129 - "undoRedo"
Cohesion: 0.33
Nodes (7): processAction(), processCell(), processRange(), redo(), setLastFocusedCell(), undo(), undoRedo()

### Community 130 - "addElementsToContainerAndGetWidth"
Cohesion: 0.40
Nodes (6): addElementsToContainerAndGetWidth(), cloneItemIntoDummy(), getAutoSizePadding(), getHeaderCellForColumn(), getPreferredWidthForColumn(), getPreferredWidthForColumnGroup()

### Community 131 - "applyCellClassRules"
Cohesion: 0.33
Nodes (6): applyCellClassRules(), applyClassesFromColDef(), getStaticCellClasses(), processAllCellClasses(), processClassRules(), processStaticCellClasses()

### Community 132 - "buildCompressedFileStream"
Cohesion: 0.40
Nodes (6): buildCompressedFileStream(), buildFileStream(), clearStream(), getUncompressedZipFile(), getZipFile(), packageFiles()

### Community 133 - "refreshHandle"
Cohesion: 0.47
Nodes (6): getHasChartRange(), isSingleCell(), onCellSelectionChanged(), refreshHandle(), updateRangeBorders(), updateRangeBordersIfRangeCount()

### Community 134 - "onFloatingFilterChanged"
Cohesion: 0.40
Nodes (6): getLastType(), onDateChanged(), onFloatingFilterChanged(), setTypeFromFloatingFilter(), setValueFromFloatingFilter(), syncUpWithParentFilter()

### Community 135 - "setAutoHeightActive"
Cohesion: 0.40
Nodes (6): isAnyParentAutoHeaderHeight(), isAutoHeaderHeight(), isAutoHeight(), isColumnInHeaderViewport(), isColumnInRowViewport(), setAutoHeightActive()

### Community 136 - "initialiseInvisibleScrollbar"
Cohesion: 0.40
Nodes (5): addActiveListenerToggles(), hideAndShowInvisibleScrollAsNeeded(), initialiseInvisibleScrollbar(), onPinnedRowDataChanged(), refreshCompBottom()

### Community 137 - "createBlock"
Cohesion: 0.50
Nodes (4): addBlock(), checkBlockToLoad(), createBlock(), loadComplete()

### Community 138 - "evaluateExpression"
Cohesion: 0.50
Nodes (4): createExpressionFunction(), createFunctionBody(), evaluate(), evaluateExpression()

### Community 139 - "processFullWidthRowKeyboardEvent"
Cohesion: 0.50
Nodes (4): createRowEvent(), processFullWidthRowKeyboardEvent(), setEditing(), setEditingRow()

### Community 140 - "resetIcons"
Cohesion: 0.67
Nodes (4): disableUserSelect(), resetIcons(), setResizeCursor(), setResizeIcons()

### Community 141 - "filterNodes"
Cohesion: 0.50
Nodes (4): doingTreeDataFiltering(), executeFromRootNode(), filter(), filterNodes()

### Community 142 - "initBeans"
Cohesion: 0.50
Nodes (4): getGridId(), initBeans(), preWireBeans(), wireBeans()

### Community 143 - "onFullWidthContainerWheel"
Cohesion: 0.50
Nodes (4): onFullWidthContainerWheel(), onStickyWheel(), scrollGridBodyToMatchEvent(), scrollVertically()

### Community 144 - "syncInRowNode"
Cohesion: 0.50
Nodes (4): setSelectedInitialValue(), syncInNewRowNode(), syncInOldRowNode(), syncInRowNode()

### Community 145 - "apiNotFound"
Cohesion: 0.67
Nodes (3): apiNotFound(), assertModuleRegistered(), makeApi()

### Community 146 - "applySizeToSiblings"
Cohesion: 0.67
Nodes (3): applySizeToSiblings(), getMinSizeOfSiblings(), getSiblings()

### Community 147 - "calculateMouseMovement"
Cohesion: 0.67
Nodes (3): calculateMouseMovement(), shouldSkipX(), shouldSkipY()

### Community 148 - "check"
Cohesion: 0.67
Nodes (3): check(), ensureCleared(), ensureTickingStarted()

### Community 149 - "getCellPositionForEvent"
Cohesion: 0.67
Nodes (3): getCellPositionForEvent(), getRenderedCellForEvent(), je()

### Community 150 - "isDomDataMissingInHierarchy"
Cohesion: 0.67
Nodes (3): getFocusCellToUseAfterRefresh(), getFocusHeaderToUseAfterRefresh(), isDomDataMissingInHierarchy()

### Community 151 - "updateGridThemeClass"
Cohesion: 0.67
Nodes (3): getGridThemeClass(), handleThemeChange(), updateGridThemeClass()

### Community 152 - "getRowByPosition"
Cohesion: 0.67
Nodes (3): getRowByPosition(), getStickyBottomRowCtrls(), getStickyTopRowCtrls()

### Community 153 - "isSuppressMenuHide"
Cohesion: 0.67
Nodes (3): isHeaderMenuButtonAlwaysShowEnabled(), isHeaderMenuButtonEnabled(), isSuppressMenuHide()

### Community 154 - "togglePickerHasFocus"
Cohesion: 0.67
Nodes (3): onPickerFocusIn(), onPickerFocusOut(), togglePickerHasFocus()

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `postConstruct()` connect `postConstruct` to `ag-grid-community.min.js`, `onDragging`, `getGui`, `createBean`, `constructor`, `R`, `addOrRemoveCssClass`, `destroyBean`, `refreshModel`, `toggle`, `setComp`, `initBeans`, `N`, `getColDef`, `getParent`, `getActualWidth`, `setWidth`, `addEventListener`, `setToDoNothing`, `m`, `p`, `ia`, `addManagedEventListeners`, `forEach`, `destroy`, `focus`, `then`, `push`, `addManagedListeners`, `addManagedElementListeners`, `setValue`, `dispatchEvent`, `get`, `isReadOnly`, `forEachInput`, `calculatePages`, `getPinned`, `isEditing`, `createPageSizeSelectorComp`, `getFrameworkOverrides`, `getLeft`, `has`, `addDestroyFunc`, `checkViewportAndScrolls`, `isAdvancedFilterEnabled`, `getViewportElement`, `Ko`, `isCheckboxSelection`, `showOverlay`, `getSort`, `setDragSource`, `refreshAriaLabelledBy`, `y`, `u`, `setupStateOnRowCountReady`, `j`, `onTabKeyDown`, `purgeBlocksIfNeeded`, `setRowTop`, `export`, `getHeaderRowCount`, `refreshCols`, `focusInnerElement`, `setupStateOnColumnsInitialised`, `getScrollFeature`, `v`, `onDisplayedColumnsChanged`?**
  _High betweenness centrality (0.003) - this node is a cross-community bridge._
- **Why does `forEach()` connect `forEach` to `ag-grid-community.min.js`, `onDragging`, `addElementsToContainerAndGetWidth`, `getGui`, `createBean`, `constructor`, `postConstruct`, `addOrRemoveCssClass`, `undoRedo`, `refreshModel`, `applyCellClassRules`, `refresh`, `createColumnEvent`, `setComp`, `N`, `initBeans`, `getParent`, `getColDef`, `getActualWidth`, `redrawAfterModelUpdate`, `addEventListener`, `setToDoNothing`, `m`, `p`, `ia`, `addManagedEventListeners`, `addEventListenersToPopup`, `destroy`, `then`, `push`, `addManagedElementListeners`, `serialize`, `getHeaderCtrls`, `get`, `forEachInput`, `getPinned`, `qt`, `createPageSizeSelectorComp`, `getFrameworkOverrides`, `onMouseDown`, `has`, `E`, `getRowNode`, `Vi`, `createOption`, `clearRowTopAndRowIndex`, `isAdvancedFilterEnabled`, `getProvidedColumnGroup`, `Ko`, `getSort`, `setParams`, `getId`, `setDragSource`, `refreshCell`, `u`, `purgeBlocksIfNeeded`, `getRow`, `forEachNode`, `updateStickyRows`, `sort`, `applyColumnState`, `checkAutoHeights`, `refreshCols`, `focusInnerElement`, `getBlocksInOrder`, `setupStateOnColumnsInitialised`, `createFooter`, `refreshNodesAndContainerHeight`, `getAllCols`, `addParentNode`, `setResizable`, `v`?**
  _High betweenness centrality (0.002) - this node is a cross-community bridge._
- **Why does `push()` connect `push` to `ag-grid-community.min.js`, `undoRedo`, `addElementsToContainerAndGetWidth`, `getGui`, `createBean`, `constructor`, `buildCompressedFileStream`, `addOrRemoveCssClass`, `postConstruct`, `refreshModel`, `R`, `createColumnEvent`, `N`, `getColDef`, `getParent`, `getActualWidth`, `redrawAfterModelUpdate`, `setWidth`, `addEventListener`, `m`, `p`, `addEventListenersToPopup`, `forEach`, `destroy`, `then`, `addManagedListeners`, `get`, `forEachInput`, `getPinned`, `qt`, `getFrameworkOverrides`, `has`, `addDestroyFunc`, `E`, `getRowNode`, `Vi`, `createOption`, `checkViewportAndScrolls`, `clearRowTopAndRowIndex`, `isAdvancedFilterEnabled`, `getColumnGroupHeaderRowHeight`, `getProvidedColumnGroup`, `getSort`, `getId`, `setDragSource`, `setRowTop`, `executeFrame`, `forEachNode`, `updateStickyRows`, `applyColumnState`, `checkAutoHeights`, `focusInnerElement`, `individualConditionPasses`, `getBlocksInOrder`, `setupStateOnColumnsInitialised`, `getColumn`, `extend`, `createFooter`, `refreshNodesAndContainerHeight`, `getAllCols`, `addParentNode`?**
  _High betweenness centrality (0.001) - this node is a cross-community bridge._
- **Should `ag-grid-community.min.js` be split into smaller, more focused modules?**
  _Cohesion score 0.008586913934961473 - nodes in this community are weakly interconnected._
- **Should `onDragging` be split into smaller, more focused modules?**
  _Cohesion score 0.05191146881287726 - nodes in this community are weakly interconnected._
- **Should `postConstruct` be split into smaller, more focused modules?**
  _Cohesion score 0.06219512195121951 - nodes in this community are weakly interconnected._
- **Should `getGui` be split into smaller, more focused modules?**
  _Cohesion score 0.08205128205128205 - nodes in this community are weakly interconnected._