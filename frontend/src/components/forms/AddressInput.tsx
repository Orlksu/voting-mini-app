import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import styles from './Form.module.css'
import { searchAddress } from '../../logic/geocodingApi.ts'
import type { AddressSuggestion } from '../../logic/geocodingApi.ts'

const SEARCH_DELAY_MS = 300
const MIN_QUERY_LENGTH = 3

type AddressInputProps = {
  value: string
  onChange: (value: string) => void
  onSelect: (suggestion: AddressSuggestion) => void
}

export default function AddressInput({
  value,
  onChange,
  onSelect,
}: AddressInputProps) {
  const [query, setQuery] = useState('')
  const [suggestions, setSuggestions] = useState<AddressSuggestion[]>([])
  const [isOpen, setIsOpen] = useState(false)
  const [isSearching, setIsSearching] = useState(false)
  const [highlighted, setHighlighted] = useState(-1)
  const [searchError, setSearchError] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const trimmed = query.trim()
    if (trimmed.length < MIN_QUERY_LENGTH) return

    const controller = new AbortController()
    const timeoutId = setTimeout(async () => {
      setIsSearching(true)
      setSearchError(null)
      try {
        const result = await searchAddress(trimmed, controller.signal)
        setSuggestions(result)
        setHighlighted(result.length > 0 ? 0 : -1)
        setIsOpen(true)
      } catch {
        if (!controller.signal.aborted) {
          setSuggestions([])
          setSearchError(
            'Поиск адреса сейчас недоступен, введите адрес вручную',
          )
        }
      } finally {
        if (!controller.signal.aborted) setIsSearching(false)
      }
    }, SEARCH_DELAY_MS)

    return () => {
      controller.abort()
      clearTimeout(timeoutId)
    }
  }, [query])

  useEffect(() => {
    if (!isOpen) return

    const handleClickOutside = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [isOpen])

  const select = (suggestion: AddressSuggestion) => {
    onSelect(suggestion)
    setIsOpen(false)
    setSuggestions([])
    setQuery('')
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen || suggestions.length === 0) return

    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setHighlighted((index) => (index + 1) % suggestions.length)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setHighlighted(
        (index) => (index - 1 + suggestions.length) % suggestions.length,
      )
    } else if (event.key === 'Enter' && highlighted >= 0) {
      event.preventDefault()
      select(suggestions[highlighted])
    } else if (event.key === 'Escape') {
      event.stopPropagation()
      setIsOpen(false)
    }
  }

  const showList = isOpen && query.trim().length >= MIN_QUERY_LENGTH

  return (
    <div className={styles.addressField} ref={containerRef}>
      <input
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
          setQuery(event.target.value)
        }}
        onFocus={() => suggestions.length > 0 && setIsOpen(true)}
        onKeyDown={handleKeyDown}
        placeholder="начните вводить: Тверская 7"
        autoComplete="off"
        role="combobox"
        aria-expanded={showList}
        aria-autocomplete="list"
        required
      />
      {isSearching && (
        <span className={`material-symbols-rounded ${styles.addressSpinner}`}>
          progress_activity
        </span>
      )}

      {showList && (
        <ul className={styles.suggestions} role="listbox">
          {suggestions.length === 0 ? (
            <li className={styles.suggestionEmpty}>ничего не найдено</li>
          ) : (
            suggestions.map((suggestion, index) => (
              <li
                key={suggestion.id}
                role="option"
                aria-selected={index === highlighted}
                className={`${styles.suggestion} ${index === highlighted ? styles.suggestionActive : ''}`}
                onMouseDown={(event) => {
                  event.preventDefault()
                  select(suggestion)
                }}
                onMouseEnter={() => setHighlighted(index)}
              >
                <span>{suggestion.label}</span>
                {suggestion.district && (
                  <span className={styles.suggestionDistrict}>
                    {suggestion.district}
                  </span>
                )}
              </li>
            ))
          )}
        </ul>
      )}

      {searchError && <p className={styles.addressError}>{searchError}</p>}
    </div>
  )
}
