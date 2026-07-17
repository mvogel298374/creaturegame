import type { ShopPrompt } from '../../hooks/useBattleHub';
import { formatItemName } from '../../battle/bag';
import { Modal } from './Modal';

// Shop node: a spend-gold buy modal. Unlike the one-shot reward pick, the shop is iterative — it stays open
// across purchases, each Buy sends BuyShopItem(index) and the balance updates live (Buy disables when the item
// costs more than the current balance). Leave ends the visit and advances the run. Prices are run-scaled ₽.
export function ShopModal({ prompt, onBuy, onLeave }: {
  prompt: ShopPrompt;
  onBuy: (index: number) => void;
  onLeave: () => void;
}) {
  return (
    <Modal label="Shop" dismiss="blocking" card="shop-modal">
      <p className="shop-title">Shop</p>
      <p className="shop-sub">Balance: <span className="shop-balance">{prompt.balance}₽</span></p>
      <div className="shop-items">
        {prompt.items.map((item, i) => {
          const affordable = item.price <= prompt.balance;
          return (
            <div
              key={i}
              className={`shop-item shop-item--${item.rarity.toLowerCase()}`}
            >
              <span className="shop-item-icon" aria-hidden="true">✦</span>
              <span className="shop-item-name">{formatItemName(item.itemName)}</span>
              <span className="shop-item-tag">{item.rarity.toUpperCase()}</span>
              <button
                className="shop-buy-btn"
                disabled={!affordable}
                onClick={() => onBuy(i)}
              >
                {item.price}₽
              </button>
            </div>
          );
        })}
      </div>
      <button className="shop-leave-btn" onClick={onLeave}>Leave</button>
    </Modal>
  );
}
